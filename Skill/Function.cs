using Alexa.NET;
using Alexa.NET.Request;
using Alexa.NET.Request.Type;
using Alexa.NET.Response;
using Alexa.NET.Response.Directive;
using Amazon.Lambda.Core;
using Application;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.DataAccess;
using Microsoft.Extensions.DependencyInjection;
using Skill.Helpers;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace Skill;

public class Function
{
    internal const string StopIntent = "AMAZON.StopIntent";
    internal const string HelpIntent = "AMAZON.HelpIntent";

    internal readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Constructor called by the lambda, inject dependencies
    /// </summary>
    public Function() : this(new Startup().ConfigureServices()) { }

    private TaskSelector TaskSelector { get; set; }
    private CurrentSession CurrentSession { get; set; }
    private ICleaningTasksRepository CleaningTasksRepository { get; set; }

    public Function(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;

        TaskSelector = serviceProvider.GetRequiredService<TaskSelector>();
        CleaningTasksRepository = serviceProvider.GetRequiredService<ICleaningTasksRepository>();

        CurrentSession = new CurrentSession(new());
    }

    /// <summary>
    /// A simple function that takes a string and does a ToUpper
    /// </summary>
    /// <param name="input">The event for the Lambda function handler to process.</param>
    /// <param name="context">The ILambdaContext that provides methods for logging and describing the Lambda environment.</param>
    /// <returns></returns>
    public async Task<SkillResponse> FunctionHandler(SkillRequest? input, ILambdaContext? context)
    {
        if (context == null || input == null)
            return ResponseBuilder.Tell("Impossible de lancer l'argent de poche. Demander de l'aide à papa.");

        CurrentSession = new CurrentSession(input.Session.Attributes);

        LogInputData(input, context);

        SkillResponse response;
        switch (input.Request)
        {
            
            case LaunchRequest lauchRequest:
                response = WelcomeUser(context);
                break;
            case IntentRequest intentRequest:
                switch (intentRequest.Intent.Name)
                {
                    case "SetFirstName":
                        response = SetFirstName(context, intentRequest);
                        break;
                    case "Accept":
                    case "AMAZON.YesIntent":
                        response = await Accept(context, intentRequest);
                        break;
                    case "Decline":
                    case "AMAZON.NoIntent":
                        response = await Decline(context, intentRequest);
                        break;
                    case "GetTodo":
                        response = await GetTodo(context, intentRequest);
                        break;
                    case "GetBalance":
                        response = await GetBalance(context, intentRequest);
                        break;
                    default:
                        response = ResponseBuilder.Tell("Au revoir.");
                        break;
                }
                break;
            default:
            case SessionEndedRequest sessionEndedRequest:
                response = ResponseBuilder.Tell("La fonction est appelée correctement.");
                break;
        }
        
        response.SessionAttributes = CurrentSession.Session;
        context.Logger.LogInformation($"Skill is answering: {System.Text.Json.JsonSerializer.Serialize(response)}");

        return response;
    }

    private async Task<SkillResponse> Accept(ILambdaContext context, IntentRequest intentRequest)
    {
        context.Logger.LogInformation($"Accepting task for {CurrentSession.LastTask}");

        var taskResult = await CleaningTasksRepository.GetTask(CurrentSession.LastTask);
        if (!taskResult.TryGetValue(out var task))
            return ResponseBuilder.Tell($"Il y a eu un problème pour récupérer la tâche {CurrentSession.LastTask}. Tu pourras retenter un peu plus tard.");

        return ResponseBuilder.Tell($"Okay, c'est noté ! Voici comment t'y prendre : " + task!.description);
    }

    private async Task<SkillResponse> Decline(ILambdaContext context, IntentRequest intentRequest)
    {
        context.Logger.LogInformation($"Declining task for {CurrentSession.LastTask}");

        var taskResult = await CleaningTasksRepository.GetTask(CurrentSession.LastTask);
        if (!taskResult.TryGetValue(out var task))
            return ResponseBuilder.Tell($"Il y a eu un problème pour récupérer la tâche {CurrentSession.LastTask}. Tu pourras retenter un peu plus tard.");

        var todoResult = await TaskSelector.GetTaskFor(new Member(CurrentSession.FirstName));
        if (!todoResult.TryGetValue(out var todo))
            return ResponseBuilder.Tell("Il y a eu un problème pour récupérer une tâche à faire. Tu pourras retenter un peu plus tard.");

        CurrentSession.LastTask = todo.name;

        var prompt = $"Tu peux {todo!.name} pour {todo.points} centimes. Acceptes-tu ou souhaites-tu une autre tâche ?";

        return ResponseBuilder.Tell($"Okay, en voici une autre : {prompt}.");
    }

    private async Task<SkillResponse> GetTodo(ILambdaContext context, IntentRequest intentRequest)
    {
        context.Logger.LogInformation($"Requesting task for {CurrentSession.FirstName}");

        var todoResult = await TaskSelector.GetTaskFor(new Member(CurrentSession.FirstName));
        if (!todoResult.TryGetValue(out var todo))
            return ResponseBuilder.Tell("Il y a eu un problème pour récupérer une tâche à faire. Tu pourras retenter un peu plus tard.");

        CurrentSession.LastTask = todo.name;

        var prompt = $"Tu peux {todo!.name} pour {todo.points} centimes. Acceptes-tu ou souhaites-tu une autre tâche ?";
        var response = ResponseBuilder.Ask(prompt, new Reprompt() { OutputSpeech = new Reprompt(prompt).OutputSpeech });

        return response;
    }

    private async Task<SkillResponse> GetBalance(ILambdaContext context, IntentRequest intentRequest)
    {
        context.Logger.LogInformation($"Requesting balance for {CurrentSession.FirstName}");

        var balanceResult = await TaskSelector.GetMemberBalance(new Member(CurrentSession.FirstName));
        if (!balanceResult.TryGetValue(out var balance))
            return ResponseBuilder.Tell("Il y a eu un problème pour récupérer ton solde d'argent de poche. Tu pourras retenter un peu plus tard.");

        context.Logger.LogInformation($"Balance found: {System.Text.Json.JsonSerializer.Serialize(balance)}");

        var amount = AmountHelper.GetAmountToPromptText(balance!.amount);
        var waiting = AmountHelper.GetWaitingAmountToPromptText(balance!.pendingAmount);
        
        var prompt = $"{amount}, et {waiting}. Si tu veux, tu peux me demander une autre tâche.";

        var response = ResponseBuilder.Ask(prompt, new Reprompt() { OutputSpeech = new Reprompt(prompt).OutputSpeech });
        return response;
    }

    private SkillResponse SetFirstName(ILambdaContext context, IntentRequest intentRequest)
    {
        string firstName = GetFirstNameFromSlot(intentRequest);

        CurrentSession.FirstName = firstName;

        var prompt = $"Bonjour {CurrentSession.FirstName}. Veux-tu une tâche à faire ou connaître ton solde d'argent de poche ?";
        var response = ResponseBuilder.Ask(prompt, new Reprompt() { OutputSpeech = new Reprompt(prompt).OutputSpeech });
        return response;
    }

    private static string GetFirstNameFromSlot(IntentRequest intentRequest)
    {
        return intentRequest.Intent.Slots.Single().Value.SlotValue.Value.Trim();
    }

    private SkillResponse WelcomeUser(ILambdaContext context)
    {
        var prompt = "Bonjour, à qui ais-je l'honneur ? Lucie, Alix ou Elio ?";
        var response = ResponseBuilder.Ask(prompt, new Reprompt() { OutputSpeech = new Reprompt(prompt).OutputSpeech });
        ExpectFirstName(response, context.Logger);
        return response;
    }

    private static void LogInputData(SkillRequest input, ILambdaContext context)
    {
        context.Logger.LogInformation($"Skill received the following session: {System.Text.Json.JsonSerializer.Serialize(input.Session)}");

        string requestSerialized = input.Request switch
        {
            IntentRequest => System.Text.Json.JsonSerializer.Serialize(input.Request as IntentRequest),
            LaunchRequest => System.Text.Json.JsonSerializer.Serialize(input.Request as LaunchRequest),
            SessionEndedRequest => System.Text.Json.JsonSerializer.Serialize(input.Request as SessionEndedRequest),
            _ => System.Text.Json.JsonSerializer.Serialize(input.Request),
        };

        context.Logger.LogInformation($"Skill received the following Intent: {requestSerialized}");
    }


    private void ExpectFirstName(SkillResponse r, ILambdaLogger logger)
    {
        r.Response.Directives.Add(new DialogElicitSlot("SetFirstName")
        {
            UpdatedIntent = new Intent
            {
                Name = "SetFirstName",
                Slots = new Dictionary<string, Slot> { { "firstName", new Slot { Name = "firstName" } } }
            }
        });

        logger.LogInformation($"Expected Next intent is : SetFirstName");
    }
}
