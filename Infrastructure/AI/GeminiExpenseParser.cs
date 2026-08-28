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

    public async Task<Result<IList<Expense>>> ParseExpenseAsync(string rawUserInput, string[] availableCategories)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent?key={_apiKey}";

        var categoriesList = string.Join(", ", availableCategories);

        var systemPrompt = $$$"""
            Tu es un assistant comptable personnel expert fonctionnant comme un parseur de données de haute précision. Ton rôle est d'analyser une saisie en langage naturel et d'en extraire TOUTES les dépenses sous forme de liste structurée.

            ### 🎯 OBJECTIF ET VALIDITÉ (`is_valid_expense`) :
            - Si la phrase mentionne un ou plusieurs achats avec des montants : extrais CHAQUE dépense distinctement dans la liste (`is_valid_expense = true`).
            - Si aucun montant n'est précisé ou si le texte n'a aucun rapport avec une dépense : crée un seul élément vide (`is_valid_expense = false`, `amount = 0`, `category = ""`, `description = ""`, `recurring_debit_id = ""`, `recurring_debit_name = ""`).

            ### 📝 RÈGLES DE FORMATAGE DES CHAMPS :
            1. **`description`** : Rédige un libellé synthétique, clair et propre, en incluant le nom de l'enseigne s'il est mentionné (ex: "Croquettes", "Courses Super U", "Clôture Lysadis").
            2. **`amount`** : Nombre décimal positif en euros (ex: 34.99).
            3. **`category`** : C'est uniquement un tag analytique. Choisis STRICTEMENT la catégorie la plus pertinente parmi cette liste : [{{categoriesList}}].
            4. **`recurring_debit_name`** : Le nom exact de la ligne choisie dans le catalogue CSV ci-dessous (ex: "Courses" ou "Plaisir").
            5. **`recurring_debit_id`** : (CRITIQUE) Renseigne OBLIGATOIREMENT le **Notion Page Id** correspondant au `recurring_debit_name` choisi. Tu dois COPIER EXACTEMENT la chaîne de 32 caractères du CSV, SANS AUCUN TIRET (`-`). N'invente pas d'ID et ne formate pas en UUID avec des tirets.

            ### 🧠 LOGIQUE D'AFFECTATION DU BUDGET (`recurring_debit_id`) :
            Le catalogue contient des budgets progressifs (achats du quotidien, `Progressif=Yes`) et des charges fixes (abonnements, prêts, assurances, `Progressif=No`). Fais preuve de déduction grâce à ces règles absolues :

            - **Déduction par le magasin / l'enseigne** : Le texte mentionne souvent le lieu d'achat. Sers-toi du type de magasin pour catégoriser la dépense vers le bon budget :
              - **Supermarchés & Alimentation** (ex: Leclerc, Super U, Intermarché, boulangerie, Chronodrive) ➔ Budget "Courses"
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
            Name,Montant,Notion Page Id,Progressif
            Abonnement Deezer,€20.00,3b7bbbc3b4e980b383f6d75b5b9bac84,No
            Abonnement Amazon,€75.00,3b7bbbc3b4e980d4a616d87f61198ff9,No
            Courses,"€1,500.00",3b7bbbc3b4e980fba81cd7ecb64c04ce,Yes
            Abonnement Netflix,€30.00,3b8bbbc3b4e980a48ab2c36d9105b095,No
            Prêt Immobilier,€820.00,3b8bbbc3b4e98052bd1aee22c92d8020,No
            Prêts Verso (Février 2027),€116.11,3b8bbbc3b4e980769702f920ba36d821,No
            Electricité,€300.00,3b8bbbc3b4e980e1a056c395a1d34e41,No
            Cantine,€200.00,3b8bbbc3b4e98055b915c9ef4d9bc998,No
            Assurance auto 1,€86.66,3b8bbbc3b4e980df8d84ccb9a976b312,No
            Abonnement téléphone Justine,€10.99,3b8bbbc3b4e980e78b23eb06186cd00d,No
            Ecole,€90.00,3b8bbbc3b4e9808f97bbca923c831516,Yes
            Cadeaux,€150.00,3b8bbbc3b4e9805ea8d4eb6c0ef32aa6,Yes
            Animaux,€300.00,3b8bbbc3b4e9807eaf3ada264a0ab699,Yes
            Carburant,€250.00,3b8bbbc3b4e9809a962ee826abccd1c0,Yes
            Santé,€50.00,3b8bbbc3b4e9802daedbe1791bccc821,Yes
            Transports,€150.00,3b8bbbc3b4e98092a9aed754b0d3db4c,Yes
            Femme de ménage,€350.00,3b8bbbc3b4e98087ac58e0f5a3e62b72,No
            Plaisir,€800.00,3b8bbbc3b4e98091ab8cf46e35a8be77,Yes
            Eau,€130.00,3b8bbbc3b4e980149d78fc450a6c2027,No
            Assurance habitation,€65.00,3b8bbbc3b4e98077ac40f6030f59d50e,No
            Imprevus,€0.00,3babbbc3b4e980c4be43eff13e7b0353,Yes
            Assurance auto 2,€64.68,3babbbc3b4e980468ba5fe58f83fdb5a,No
            Prêt Pompe à chaleur,€44.75,3babbbc3b4e980579abbe4cff135bdc8,No
            Prêt Auris (Juillet 2027),€475.57,3babbbc3b4e980058e78dfedfc5cd203,No
            Starlink,€45.00,3babbbc3b4e9802c8a2dcaa7cd00304d,No
            Abonnement Telephone Adrien,€6.99,3babbbc3b4e98027bb81d870e6ca2617,No
            Taxe foncière Lieuron,€77.00,3babbbc3b4e98072b16bf8982d374abc,No
            Accompte Impôts revenus indépendants,€94.00,3babbbc3b4e980cd86e8c6b15e01dd59,No
            Taxe foncière Asnières,€69.00,3babbbc3b4e9807cbc8ac276bce157ba,No
            Abonnement Canva,€12.00,3babbbc3b4e98074a086eab287871888,No
            Abonnement Gemini,€10.99,3babbbc3b4e9806db433f9093bc00836,No
            Abonnement nom de domaine justine-dieteticienne,€12.00,3babbbc3b4e980d891e7cb0de89a03ce,No
            Abonnement Hébergement site Justine,€115.00,3babbbc3b4e9802a84d4c7472d5399e5,No
            Maréchal,€100.00,3babbbc3b4e98050bd2af89429bc2e35,No
            Assurance RC pro Justine,€11.32,3babbbc3b4e980779902efb063563877,No
            Médecins du Monde,€9.00,3bbbbbc3b4e980efa01ee0e7637a36d3,No
            La croix rouge,€9.00,3bbbbbc3b4e9802f9f21f5216b5f5708,No
            Argent de poche enfants,€20.00,3bbbbbc3b4e980aa9501cb18699fd0ae,Yes
            Carte transport Justine,€129.60,3bbbbbc3b4e98089bf23d2876ec85156,No
            Lunii,€11.90,3bbbbbc3b4e980dfadcee70bb32d2c0f,No
            Taxe ordures ménagères (Mai / Juillet / Septembre / Novembre),€81.10,3bbbbbc3b4e980d59fdcf07c6f1b5492,No
            "Sport enfants ",€50.00,3bbbbbc3b4e98043b1cbcef069d967b3,No
            Epargne enfants,€130.00,3bbbbbc3b4e9807781f3c9001e1327cf,No

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
              Output: amount=75.00, category="Abonnement numétique", description="Abonnement Amazon Prime", recurring_debit_id="3b7bbbc3b4e980d4a616d87f61198ff9", recurring_debit_name="Abonnement Amazon", is_valid_expense=true
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
                    type = "ARRAY",
                    items = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            amount = new { type = "NUMBER", description = "Expense amount in euros" },
                            category = new { type = "STRING", description = "Category matching the predefined list" },
                            description = new { type = "STRING", description = "Short label/description" },
                            recurring_debit_id = new { type = "STRING", description = "The recurring debit id to associate to the expense" },
                            recurring_debit_name = new { type = "STRING", description = "The recurring debit name to associate to the expense" }
                        },
                        required = new[] { "amount", "category", "description", "recurring_debit_id", "recurring_debit_name" }
                    }
                }
            }
        };

        var response = await _httpClient.PostAsJsonAsync(url, payload);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            return Result.Failure<IList<Expense>>($"Gemini API Error ({response.StatusCode}): {errorBody}");
        }

        var jsonResponse = await response.Content.ReadFromJsonAsync<GeminiApiResponse>();
        var generatedJson = jsonResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

        
        var expenses = JsonSerializer.Deserialize<IList<Expense>>(generatedJson ?? string.Empty);
        if (expenses == null || expenses.Count == 0)
            return Result.Failure<IList<Expense>>("No response from Gemini");

        return Result.Success(expenses);
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