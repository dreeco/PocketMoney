using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using Domain.BudgetEntities;

namespace Infrastructure.AI;

public class GeminiExpenseParser
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;


    public GeminiExpenseParser(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                  ?? throw new InvalidOperationException("GEMINI_API_KEY is not configured.");
    }

    public async Task<Result<RouteAction>> ParseRouteFromMessage(string rawUserInput)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent?key={_apiKey}";

        var systemPrompt = $$$"""
            Tu es chargé de pré-trier un message pour savoir ce que mon application de budget doit effectuer comme opération.
            Tu reçois un message en language naturel d'un utilisateur qui peut être :
            - Une dépense (SaisieDépense). C'est l'opération la plus courante reçue dans le système. Exemple: "12 euros chez Gifi", "64.41 voiture garage", "19.99 abonnement Deezer"
            - Un revenu (SaisieRevenu). Il y aura mention de "Salaire", "Remboursement", "Aides", "CAF"
            - Une demande de situation (RésuméSituation). Ce sera un message sans chiffre avec par exemple : "résumé", "situation", "mois courant", etc.

            Tu dois donner l'une de ces 3 actions obligatoirement [SaisieDépense, SaisieRevenu, RésuméSituation] sinon le système ne pourra pas la traiter.
            """;

        // Définition de la payload avec contrainte de réponse JSON (Structured Output)
        var payload = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = rawUserInput } }
                }
            },
            system_instruction = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            generationConfig = new
            {
                response_mime_type = "application/json",
                response_schema = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        action = new { type = "STRING", description = "Action that the user wants to perform in the following list : [SaisieDépense, SaisieRevenu, RésuméSituation]" },
                    },
                    required = new[] { "action" }
                }
            }
        };

        var response = await _httpClient.PostAsJsonAsync(url, payload);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            return Result.Failure<RouteAction>($"Gemini API Error ({response.StatusCode}): {errorBody}");
        }

        var jsonResponse = await response.Content.ReadFromJsonAsync<GeminiApiResponse>();
        var generatedJson = jsonResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;


        var routeAction = JsonSerializer.Deserialize<RouteAction>(generatedJson ?? string.Empty);
        if (routeAction == null)
            return Result.Failure<RouteAction>("No response from Gemini");

        return Result.Success(routeAction);
    }

    public async Task<Result<Expense>> ParseExpenseAsync(string rawUserInput, string recurringDebits)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent?key={_apiKey}";

        // Vos catégories prédéfinies pour Notion
        string[] availableCategories =
        [
            "Alimentation",
            "Animaux",
            "Assurances",
            "Cadeaux",
            "Dons",
            "Frais bancaires",
            "Frais maison",
            "Habits",
            "Impôts",
            "Internet",
            "Santé",
            "Sorties",
            "Travail",
            "Vacances",
            "Voiture"
        ];

        var categoriesList = string.Join(", ", availableCategories);

        var systemPrompt = $$"""
            Tu es un assistant comptable personnel expert fonctionnant comme un parseur de données de haute précision. Ton rôle est d'analyser une saisie en langage naturel et d'en extraire la dépense sous forme de donnée structurée.

            ### 🎯 OBJECTIF ET VALIDITÉ (`is_valid_expense`) :
            - Si la phrase mentionne un ou plusieurs achats avec des montants : extrais CHAQUE dépense distinctement dans la liste (`is_valid_expense = true`).
            - Si aucun montant n'est précisé ou si le texte n'a aucun rapport avec une dépense : crée un seul élément vide (`is_valid_expense = false`, `amount = 0`, `category = ""`, `description = ""`, `recurring_debit_id = ""`, `recurring_debit_name = ""`).

            ### 📝 RÈGLES DE FORMATAGE DES CHAMPS :
            1. **`description`** : Rédige un libellé synthétique, clair et propre, en incluant le nom de l'enseigne s'il est mentionné (ex: "Croquettes", "Courses Super U", "Clôture Lysadis").
            2. **`amount`** : Nombre décimal positif en euros (ex: 34.99).
            3. **`category`** : C'est uniquement un tag analytique. Choisis STRICTEMENT la catégorie la plus pertinente parmi cette liste : [{{categoriesList}}].
            4. **`recurring_debit_name`** : Le nom exact de la ligne choisie dans le catalogue CSV ci-dessous (ex: "Courses" ou "Plaisir").
            5. **`recurring_debit_id`** : (CRITIQUE) Renseigne OBLIGATOIREMENT le **Notion Page Id** correspondant au `recurring_debit_name` choisi. Tu dois COPIER EXACTEMENT la chaîne de 32 caractères du CSV, SANS AUCUN TIRET (`-`). N'invente pas d'ID et ne formate pas en UUID avec des tirets.
            6. **`is_transfer`** : Vrai si la dépense est faite par chèque / virement / retrait d'espèces

            ### 🧠 LOGIQUE D'AFFECTATION DU BUDGET (`recurring_debit_id`) :
            Le catalogue contient des budgets progressifs (achats du quotidien, `Progressif=Yes`) et des charges fixes (abonnements, prêts, assurances, `Progressif=No`). Fais preuve de déduction grâce à ces règles absolues :

            - **Déduction par le magasin / l'enseigne** : Le texte mentionne souvent le lieu d'achat. Sers-toi du type de magasin pour catégoriser la dépense vers le bon budget :
              - **Supermarchés & Alimentation** (ex: Leclerc, Super U, Intermarché, boulangerie, Chronodrive, Ferme) ➔ Budget "Courses"
              - **Bricolage & Jardinage** (ex: Lysadis, Leroy Merlin, Castorama) ➔ Budget "Plaisir"
              - **Équipement de la maison & Décoration** (ex: Gifi, Ikea, But, Action) ➔ Budget "Plaisir"
              - **Vêtements & Mode** (ex: Zalando, Kiabi, boutiques de vêtements) ➔ Budget "Plaisir"
              - **Pharmacies & Médecins** ➔ Budget "Santé"
              - **Animaleries & Vétérinaires** (ex: Maxi Zoo) ➔ Budget "Animaux"
              - **Stations-service & Garages** (ex: Total, Esso) ➔ Budget "Carburant"

            - **Règle d'or des plateformes (ex: Amazon)** : Un achat de biens (ex: "Lait végétal Amazon 60.59") va dans le budget "Courses" ou "Plaisir" (Progressif=Yes). Le budget fixe "Abonnement Amazon" (Progressif=No) est STRICTEMENT réservé au paiement de l'abonnement Prime lui-même (montant proche de 75€).
            - **Cadeaux offerts à des tiers** ➔ Budget "Cadeaux"
            - **Abonnements / Prêts / Assurances** ➔ Cherche la correspondance exacte dans les lignes `Progressif=No`. Utilise le montant indicatif du catalogue pour t'aider à arbitrer en cas d'ambiguïté (ex: différencier Assurance auto 1 et Assurance auto 2).
            - **Dépense inclassable, sortie, resto ou achat non-essentiel** ➔ Utilise par défaut le budget "Plaisir".

            ### 📊 CATALOGUE DES DÉPENSES RÉCURRENTES (CSV) :
            {{recurringDebits}}

            ### 💡 EXEMPLES DE MAPPINGS ATTENDUS :
            - Input: "120.50 cloture lysadis"
              Output: amount=120.50, category="Maison", description="Clôture Lysadis", recurring_debit_id="3b8bbbc3b4e98091ab8cf46e35a8be77", recurring_debit_name="Plaisir", is_valid_expense=true
            - Input: "Gifi 34,98 verres + brome"
              Output: amount=34.98, category="Maison", description="Verres + brome Gifi", recurring_debit_id="3b8bbbc3b4e98091ab8cf46e35a8be77", recurring_debit_name="Plaisir", is_valid_expense=true
            - Input: "Lait végétal Amazon 60.59"
              Output: amount=60.59, category="Alimentation", description="Lait végétal Amazon", recurring_debit_id="3b7bbbc3b4e980fba81cd7ecb64c04ce", recurring_debit_name="Courses", is_valid_expense=true
            - Input: "34.99 croquettes et 12.50 burger king"
              Output 1: amount=34.99, category="Animaux", description="Croquettes", recurring_debit_id="3b8bbbc3b4e9807eaf3ada264a0ab699", recurring_debit_name="Animaux", is_valid_expense=true
              Output 2: amount=12.50, category="Sorties", description="Burger King", recurring_debit_id="3b8bbbc3b4e98091ab8cf46e35a8be77", recurring_debit_name="Plaisir", is_valid_expense=true
            - Input: "Abo amazon"
              Output: amount=75.00, category="Abonnement numérique", description="Abonnement Amazon Prime", recurring_debit_id="3b7bbbc3b4e980d4a616d87f61198ff9", recurring_debit_name="Abonnement Amazon", is_valid_expense=true
            - Input: "Salut le bot"
              Output: is_valid_expense=false, amount=0, category="", description="", recurring_debit_id="", recurring_debit_name=""
            """;

        // Définition de la payload avec contrainte de réponse JSON (Structured Output)
        var payload = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = rawUserInput } }
                }
            },
            system_instruction = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            generationConfig = new
            {
                response_mime_type = "application/json",
                response_schema = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        amount = new { type = "NUMBER", description = "Le montant de la dépense en euros" },
                        category = new { type = "STRING", description = $"Une des catégories de cette liste : [{String.Join(", ", availableCategories)}]" },
                        description = new { type = "STRING", description = "Une courte description / label" },
                        recurring_debit_id = new { type = "STRING", description = "L'id de la dépense récurrente parmis le catalogue des dépenses récurrentes" },
                        recurring_debit_name = new { type = "STRING", description = "Le nom de la dépense récurrente parmis le catalogue des dépenses récurrentes" },
                        is_transfer = new { type = "BOOLEAN", description = "Vrai si la dépense est faite par chèque, virement, espèces. Sinon faux." },
                        is_valid_expense = new { type = "BOOLEAN", description = "Vrai si le travail de parsing de la dépense a pu extraire les informations correctement." }
                    },
                    required = new[] { "amount", "category", "description", "recurring_debit_id", "recurring_debit_name", "is_valid_expense" }
                }
            }
        };

        var response = await _httpClient.PostAsJsonAsync(url, payload);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            return Result.Failure<Expense>($"Gemini API Error ({response.StatusCode}): {errorBody}");
        }

        var jsonResponse = await response.Content.ReadFromJsonAsync<GeminiApiResponse>();
        var generatedJson = jsonResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

        var expense = JsonSerializer.Deserialize<Expense>(generatedJson ?? string.Empty);
        if (expense == null)
            return Result.Failure<Expense>("No response from Gemini");

        return Result.Success(expense);
    }


    public async Task<Result<Expense>> ParseIncomeAsync(string rawUserInput, string recurringCredits)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent?key={_apiKey}";

        // Vos catégories prédéfinies pour Notion
        string[] availableCategories =
        [
            "Remboursement",
            "Aides",
            "Salaire",
        ];

        var categoriesList = string.Join(", ", availableCategories);

        var systemPrompt = $$"""
            Tu es un assistant comptable personnel expert fonctionnant comme un parseur de données de haute précision. Ton rôle est d'analyser une saisie en langage naturel et d'en extraire la dépense sous forme de donnée structurée.

            ### 🎯 OBJECTIF ET VALIDITÉ (`is_valid_expense`) :
            - Si la phrase mentionne un ou plusieurs revenu avec des montants : extrais CHAQUE dépense distinctement dans la liste (`is_valid_expense = true`).
            - Si aucun montant n'est précisé ou si le texte n'a aucun rapport avec une dépense : crée un seul élément vide (`is_valid_expense = false`, `amount = 0`, `category = ""`, `description = ""`, `recurring_debit_id = ""`, `recurring_debit_name = ""`).

            ### 📝 RÈGLES DE FORMATAGE DES CHAMPS :
            1. **`description`** : Rédige un libellé synthétique, clair et propre, en incluant le nom de l'enseigne s'il est mentionné (ex: "Croquettes", "Courses Super U", "Clôture Lysadis").
            2. **`amount`** : Nombre décimal positif en euros (ex: 34.99).
            3. **`category`** : C'est uniquement un tag analytique. Choisis STRICTEMENT la catégorie la plus pertinente parmi cette liste : [{{categoriesList}}].
            4. **`recurring_debit_name`** : Le nom exact de la ligne choisie dans le catalogue CSV QUAND UN REVENU RECURRENT MATCHE ci-dessous (ex: "Courses" ou "Plaisir").
            5. **`recurring_debit_id`** : Renseigne QUAND UN REVENU RECURRENT MATCHE le **Notion Page Id** correspondant au `recurring_debit_name` choisi. Tu dois COPIER EXACTEMENT la chaîne de 32 caractères du CSV, SANS AUCUN TIRET (`-`). N'invente pas d'ID et ne formate pas en UUID avec des tirets.
            6. **`is_transfer`** : Vrai si le revenu est sous forme de chèque / virement / retrait d'espèces ou si un `recurring_credit_id` a été trouvé

            Le matche des revenus récurrents n'est pas obligatoire, il y a très peu de revenus récurrents.
            ### 📊 CATALOGUE DES REVENUS RÉCURRENTES (CSV) :
            {{recurringCredits}}

            ### 💡 EXEMPLES DE MAPPINGS ATTENDUS :
            - Input: "437 CAF"
              Output: amount=437, category="Aides", description="Aide CAF", recurring_credit_id="3b9bbbc3b4e980e08ba6ce81fc647979", recurring_debit_name="CAF", is_valid_expense=true
            - Input: "Salaire Adrien 5349.76"
              Output: amount=5349.76, category="Salaire", description="Salaire Adrien", recurring_debit_id="3b9bbbc3b4e980399630d21f3160f46e", recurring_debit_name="Salaire Adrien", is_valid_expense=true
            - Input: "Remboursement restaurant Nico 78.48"
              Output: amount=78.48, category="Remboursement", description="Remboursement restaurant Nicolas", recurring_debit_id="", recurring_debit_name="", is_valid_expense=true
            - Input: "Salut le bot"
              Output: is_valid_expense=false, amount=0, category="", description="", recurring_debit_id="", recurring_debit_name=""
            """;

        // Définition de la payload avec contrainte de réponse JSON (Structured Output)
        var payload = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = rawUserInput } }
                }
            },
            system_instruction = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            generationConfig = new
            {
                response_mime_type = "application/json",
                response_schema = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        amount = new { type = "NUMBER", description = "Le montant de la dépense en euros" },
                        category = new { type = "STRING", description = $"Une des catégories de cette liste : [{String.Join(", ", availableCategories)}]" },
                        description = new { type = "STRING", description = "Une courte description / label" },
                        recurring_debit_id = new { type = "STRING", description = "L'id de la dépense récurrente parmis le catalogue des dépenses récurrentes" },
                        recurring_debit_name = new { type = "STRING", description = "Le nom de la dépense récurrente parmis le catalogue des dépenses récurrentes" },
                        is_transfer = new { type = "BOOLEAN", description = "Vrai si la dépense est faite par chèque, virement, espèces. Sinon faux." },
                        is_valid_expense = new { type = "BOOLEAN", description = "Vrai si le travail de parsing de la dépense a pu extraire les informations correctement." }
                    },
                    required = new[] { "amount", "category", "description", "recurring_debit_id", "recurring_debit_name", "is_valid_expense" }
                }
            }
        };

        var response = await _httpClient.PostAsJsonAsync(url, payload);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            return Result.Failure<Expense>($"Gemini API Error ({response.StatusCode}): {errorBody}");
        }

        var jsonResponse = await response.Content.ReadFromJsonAsync<GeminiApiResponse>();
        var generatedJson = jsonResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

        var expense = JsonSerializer.Deserialize<Expense>(generatedJson ?? string.Empty);
        if (expense == null)
            return Result.Failure<Expense>("No response from Gemini");

        return Result.Success(expense);
    }


    public async Task<Result<Situation>> EvaluateSituation(string rawUserInput, string recurringDebits, string billingMonths)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent?key={_apiKey}";

        var systemPrompt = $$"""
            Tu es un assistant comptable personnel expert fonctionnant comme un analyseur d'application budgétaire. Ton rôle est d'analyser une saisie en langage naturel et d'en extraire une réponse claire, succinte (car lue par message) et etayée ave les données de l'application budgétaire. 
            Tu répondras sous forme de donnée structurée.
            Si un mois en mentionné, utilise le bilan mensuel du mois mentionné, sinon le bilan du mois courant.
            
            ### ⚠️ Points d'attention
            - Comme nous sommes en débit différé sur nos cartes bancaires, les budgets vont du 20 au 19 du mois suivant. Par exemple le 25 juillet 2026 tombe dans le budget Aoû 2026, sauf si il s'agit d'un prélèvement. Le budget "Septembre 2026" couvre donc la période 20 août 2026 au 19 septembre 2026.
            - Les dépenses progressives sont des budgets (courses, plaisir, etc.) donc fait attention à quand tombent les dépenses pour savoir de quel bilan mesnuel il s'agit.
            - Les dépenses non progressives sont des prélèvements automatiques, certains sur carte (donc du 20 au 20), certains par virement donc en fonction de leur jour de prélèvement ils tombent dans le mois donné sans prendre en compte la règle allant du 20 au 20.
            - On distingue les dates de prélèvement des dates d'apparition CIC pour pouvoir comparer aux comptes, tu peux ignorer les colonnes qui mentionnent CIC.
            
            ### 📊 Bilans mensuels mois passés, l'actuel ainsi que les anticipations futures au format CSV:
            {{billingMonths}}
            
            ### 📊 Catalogue des dépenses récurrentes et de leur état ACTUEL
            {{recurringDebits}}
            
            ### 💡 EXEMPLES DE MAPPINGS ATTENDUS :
            - "Prélèvements à venir": répond avec un état du mois actuel et la liste des dépenses non progressives à venir. Résume le montant qui doit être débité.
            - "Etats des budgets" : Concerne les dépenses récurrentes dîtes "Progressives" (sans montant fixe) donne l'état actuel et ce que l'on peut se permettre comme dépenses ce mois-ci.
            
            Dans les informations courantes importantes : 
            - Combien nous restera-t-il à la fin du mois (le 20 de chaque mois) avec l'évaluation de nos dépenses
            - Avons nous explosé les budgets ?
            - Quelle poste de dépense important est encore à venir ?
            """;

        // Définition de la payload avec contrainte de réponse JSON (Structured Output)
        var payload = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = rawUserInput } }
                }
            },
            system_instruction = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            generationConfig = new
            {
                response_mime_type = "application/json",
                response_schema = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        summary = new { type = "STRING", description = "Une réponse textuelle faite pour être lue dans un message sur l'application Telegram. Tu peux utiliser des émojis et un peu de mise en page au format markdown" },
                    },
                    required = new[] { "summary" }
                }
            }
        };

        var response = await _httpClient.PostAsJsonAsync(url, payload);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            return Result.Failure<Situation>($"Gemini API Error ({response.StatusCode}): {errorBody}");
        }

        var jsonResponse = await response.Content.ReadFromJsonAsync<GeminiApiResponse>();
        var generatedJson = jsonResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

        var situation = JsonSerializer.Deserialize<Situation>(generatedJson ?? string.Empty);
        if (situation == null)
            return Result.Failure<Situation>("No response from Gemini");

        return Result.Success(situation);
    }
}

// Modèles internes pour désérialiser l'enveloppe de l'API Gemini
internal class GeminiApiResponse
{
    [JsonPropertyName("candidates")]
    public List<Candidate>? Candidates { get; set; }
}

internal class Candidate
{
    [JsonPropertyName("content")]
    public GeminiContent? Content { get; set; }
}

internal class GeminiContent
{
    [JsonPropertyName("parts")]
    public List<GeminiPart>? Parts { get; set; }
}

internal class GeminiPart
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}