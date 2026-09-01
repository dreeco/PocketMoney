using CSharpFunctionalExtensions;
using Domain.BudgetEntities;
using Domain.Services;
using Google.GenAI;
using Google.GenAI.Types;
using System.Text.Json;
using Type = Google.GenAI.Types.Type;

namespace Infrastructure.AI;

public class GenAiBudgetService : IGenAiBudgetService
{
    private const string DefaultLLM = "gemini-3.5-flash-lite";
    private Client _client;

    public GenAiBudgetService(string apiKey)
    {
        _client = new Client(apiKey: apiKey);
    }

    public async Task<Result<Situation>> EvaluateSituation(string rawUserInput, string recurringDebits, string billingMonths)
    {
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

        var config = new GenerateContentConfig
        {
            SystemInstruction = new Content
            {
                Parts = new List<Part> { new() { Text = systemPrompt } }
            },
            ResponseMimeType = "application/json",
            ResponseSchema = new Schema
            {
                Type = Type.Object,
                Properties = new Dictionary<string, Schema>
                {
                    ["summary"] = new Schema
                    {
                        Type = Type.String,
                        Description = "Une réponse textuelle faite pour être lue dans un message sur l'application Telegram."
                    }
                },
                Required = new List<string> { "summary" }
            }
        };

        return await RunPrompt<Situation>(rawUserInput, config);
    }

    public async Task<Result<Expense>> ParseExpenseAsync(string rawUserInput, string recurringDebits)
    {
        string[] availableCategories = ["Alimentation", "Animaux", "Assurances", "Cadeaux", "Dons", "Frais bancaires", "Frais maison",
            "Habits", "Impôts", "Internet", "Santé", "Sorties", "Travail", "Vacances", "Voiture"];

        var categoriesList = string.Join(", ", availableCategories);

        var systemPrompt = $$"""
            Tu es un assistant comptable personnel expert fonctionnant comme un parseur de données de haute précision. Ton rôle est d'analyser une saisie en langage naturel et d'en extraire la dépense sous forme de donnée structurée.

            ### 🎯 OBJECTIF ET VALIDITÉ (`is_valid_expense`) :
            - Si aucun montant n'est précisé ou si le texte n'a aucun rapport avec une dépense : utilise `is_valid_expense = false` pour le mettre en avant.
            
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
        
        var config = GetExpenseConfig(categoriesList, systemPrompt, "la dépense");

        return await RunPrompt<Expense>(rawUserInput, config);
    }

    private static GenerateContentConfig GetExpenseConfig(string categoriesList, string systemPrompt, string type)
    {
        return new GenerateContentConfig
        {
            SystemInstruction = new Content
            {
                Parts = new List<Part> { new() { Text = systemPrompt } }
            },
            ResponseMimeType = "application/json",
            ResponseSchema = new Schema
            {
                Type = Type.Object,
                Properties = new Dictionary<string, Schema>
                {
                    ["amount"] = new Schema { Type = Type.Number, Description = $"Le montant de {type} en euros" },
                    ["category"] = new Schema { Type = Type.String, Description = $"Une de ces catégories : [{categoriesList}]" },
                    ["description"] = new Schema { Type = Type.String, Description = "Une courte description / label" },
                    ["recurring_debit_id"] = new Schema { Type = Type.String, Description = $"L'id de {type} récurrente parmis le catalogue" },
                    ["recurring_debit_name"] = new Schema { Type = Type.String, Description = $"Le nom de {type} récurrente parmis le catalogue" },
                    ["is_transfer"] = new Schema { Type = Type.Boolean, Description = "Vrai si chèque, virement ou espèces." },
                    ["is_valid_expense"] = new Schema { Type = Type.Boolean, Description = "Vrai si le parsing a réussi." }
                },
                Required = new List<string> { "amount", "category", "description", "recurring_debit_id", "recurring_debit_name", "is_valid_expense" }
            }
        };
    }

    public async Task<Result<Expense>> ParseIncomeAsync(string rawUserInput, string recurringCredits)
    {
        string[] availableCategories = ["Remboursement", "Aides", "Salaire"];

        var categoriesList = string.Join(", ", availableCategories);

        var systemPrompt = $$"""
            Tu es un assistant comptable personnel expert fonctionnant comme un parseur de données de haute précision. Ton rôle est d'analyser une saisie en langage naturel et d'en extraire la dépense sous forme de donnée structurée.

            ### 🎯 OBJECTIF ET VALIDITÉ (`is_valid_expense`) :
            - Si aucun montant n'est précisé ou si le texte n'a aucun rapport avec uneun revenu à enregistrer : utilise `is_valid_expense = false` pour le mettre en avant.
            
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

        var config = GetExpenseConfig(categoriesList, systemPrompt, "le révenu");

        return await RunPrompt<Expense>(rawUserInput, config);
    }

    public async Task<Result<RouteAction>> ParseRouteFromMessage(string rawUserInput)
    {
        var systemPrompt = """
            Tu es chargé de pré-trier un message pour savoir ce que mon application de budget doit effectuer comme opération.
            Tu reçois un message en language naturel d'un utilisateur qui peut être :
            - Une dépense (SaisieDépense). C'est l'opération la plus courante reçue dans le système. Exemple: "12 euros chez Gifi", "64.41 voiture garage", "19.99 abonnement Deezer"
            - Un revenu (SaisieRevenu). Il y aura mention de "Salaire", "Remboursement", "Aides", "CAF"
            - Une demande de situation (RésuméSituation). Ce sera un message sans chiffre avec par exemple : "résumé", "situation", "mois courant", etc.

            Tu dois donner l'une de ces 3 actions obligatoirement [SaisieDépense, SaisieRevenu, RésuméSituation] sinon le système ne pourra pas la traiter.
            """;

        var config = new GenerateContentConfig
        {
            SystemInstruction = new Content
            {
                Parts = new List<Part> { new() { Text = systemPrompt } }
            },
            ResponseMimeType = "application/json",
            ResponseSchema = new Schema
            {
                Type = Type.Object,
                Properties = new Dictionary<string, Schema>
                {
                    ["action"] = new Schema
                    {
                        Type = Type.String,
                        Description = "Action à effectuer par le système",
                        Enum = new List<string> { "SaisieDépense", "SaisieRevenu", "RésuméSituation" }
                    }
                },
                Required = new List<string> { "action" }
            }
        };

        return await RunPrompt<RouteAction>(rawUserInput, config);
    }

    private async Task<Result<T>> RunPrompt<T>(string rawUserInput, GenerateContentConfig config)
    {
        try
        {
            var response = await _client.Models.GenerateContentAsync(
                model: DefaultLLM,
                contents: rawUserInput,
                config: config
            );

            var jsonText = response.Text;
            if (string.IsNullOrWhiteSpace(jsonText))
            {
                return Result.Failure<T>("No text received from Gemini API.");
            }

            var situation = JsonSerializer.Deserialize<T>(jsonText);
            return situation != null
                ? Result.Success(situation)
                : Result.Failure<T>("Failed to deserialize response into Situation object.");
        }
        catch (Exception ex)
        {
            return Result.Failure<T>($"Gemini API Exception: {ex.Message}");
        }
    }
}
