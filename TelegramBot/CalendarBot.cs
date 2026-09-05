using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.GenAI;
using Google.GenAI.Types;

class Program
{
    // Modèle de données attendu par Gemini
    public record ParsedEvent(
        [property: JsonPropertyName("summary")] string Summary,
        [property: JsonPropertyName("location")] string? Location,
        [property: JsonPropertyName("startIso")] string StartIso,
        [property: JsonPropertyName("endIso")] string EndIso,
        [property: JsonPropertyName("description")] string? Description
    );

    public static async Task Test()
    {
        string promptUtilisateur = "dentiste mardi 14h30 à Vannes pendant 45 min";
        string geminiApiKey = System.Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "VOTRE_CLE_AI_STUDIO";

        Console.WriteLine("1. Analyse et extraction via Gemini...");
        var parsedEvent = await ExtractEventDetailsAsync(geminiApiKey, promptUtilisateur);

        Console.WriteLine($"-> Titre : {parsedEvent.Summary}");
        Console.WriteLine($"-> Début : {parsedEvent.StartIso}");
        Console.WriteLine($"-> Lieu  : {parsedEvent.Location}");

        Console.WriteLine("\n2. Ajout au Google Calendar...");
        var calendarService = await GetCalendarServiceAsync("credentials.json"); // Téléchargé depuis la console GCP
        var createdEvent = await InsertEventAsync(calendarService, parsedEvent);

        Console.WriteLine($"Succès ! Événement créé : {createdEvent.HtmlLink}");
    }

    /// <summary>
    /// Utilise Gemini avec Structured Outputs pour contraindre la réponse en JSON strict.
    /// </summary>
    private static async Task<ParsedEvent> ExtractEventDetailsAsync(string apiKey, string userInput)
    {
        var client = new Client(apiKey: apiKey);

        // Date du jour injectée dans le contexte système pour résoudre les dates relatives ("mardi", "demain")
        string systemInstruction = $"""
            Tu es un assistant de calendrier. La date et heure actuelles de référence sont : {DateTime.Now:yyyy-MM-dd HH:mm:ss (dddd)}.
            Fuseau horaire : Europe/Paris (+01:00 ou +02:00 selon la saison).
            Extrais les informations de l'événement au format ISO 8601 strict pour startIso et endIso.
            Si la durée n'est pas spécifiée, prévois 1 heure par défaut.
            """;

        var config = new GenerateContentConfig
        {
            SystemInstruction = new Content { Parts = [new Part { Text = systemInstruction }] },
            ResponseMimeType = "application/json",
            ResponseSchema = new Schema
            {
                Type = "OBJECT",
                Properties = new Dictionary<string, Schema>
                {
                    ["summary"] = new() { Type = "STRING", Description = "Titre clair de l'événement" },
                    ["location"] = new() { Type = "STRING", Description = "Lieu si précisé, sinon null" },
                    ["startIso"] = new() { Type = "STRING", Description = "Date et heure de début en format ISO 8601 (ex: 2026-09-08T14:30:00+02:00)" },
                    ["endIso"] = new() { Type = "STRING", Description = "Date et heure de fin en format ISO 8601" },
                    ["description"] = new() { Type = "STRING", Description = "Notes complémentaires si utiles, sinon null" }
                },
                Required = ["summary", "startIso", "endIso"]
            }
        };

        var response = await client.Models.GenerateContentAsync(
            model: "gemini-2.5-flash",
            contents: [new Content { Parts = [new Part { Text = userInput }] }],
            config: config
        );

        return JsonSerializer.Deserialize<ParsedEvent>(response.Text!)!;
    }

    /// <summary>
    /// Authentifie l'utilisateur via OAuth2 (ouvre un onglet navigateur lors du premier lancement).
    /// </summary>
    private static async Task<CalendarService> GetCalendarServiceAsync(string clientSecretsPath)
    {
        using var stream = new FileStream(clientSecretsPath, FileMode.Open, FileAccess.Read);

        // Scope requis pour ajouter/modifier des événements
        string[] scopes = [CalendarService.Scope.CalendarEvents];

        UserCredential credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            GoogleClientSecrets.FromStream(stream).Secrets,
            scopes,
            "user",
            CancellationToken.None,
            new FileDataStore("GoogleCalendarTokenStore", true)
        );

        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "GeminiCalendarIntegration"
        });
    }

    /// <summary>
    /// Insère l'événement dans le calendrier principal de l'utilisateur.
    /// </summary>
    private static async Task<Event> InsertEventAsync(CalendarService service, ParsedEvent data)
    {
        var calendarEvent = new Event
        {
            Summary = data.Summary,
            Location = data.Location,
            Description = data.Description,
            Start = new EventDateTime
            {
                DateTimeRaw = data.StartIso,
                TimeZone = "Europe/Paris"
            },
            End = new EventDateTime
            {
                DateTimeRaw = data.EndIso,
                TimeZone = "Europe/Paris"
            }
        };

        // "primary" cible l'agenda principal de l'utilisateur connecté
        var request = service.Events.Insert(calendarEvent, "primary");
        return await request.ExecuteAsync();
    }
}