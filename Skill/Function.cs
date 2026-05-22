using Alexa.NET;
using Alexa.NET.Request;
using Alexa.NET.Request.Type;
using Alexa.NET.Response;
using Alexa.NET.Response.Directive;
using Amazon.Lambda.Core;
using Application;
using Domain.Entities;
using Domain.Repositories;
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

    public Function(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;

        TaskSelector = serviceProvider.GetRequiredService<TaskSelector>();

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


        switch (input.Request)
        {
            case LaunchRequest lauchRequest:
                return WelcomeUser(context);
            case IntentRequest intentRequest:
                switch (intentRequest.Intent.Name)
                {
                    case "SetFirstName":
                        return SetFirstName(context, intentRequest);
                    case "GetTodo":
                        return await GetTodo(context, intentRequest);
                    case "GetBalance":
                        return await GetBalance(context, intentRequest);
                    default:
                        return ResponseBuilder.Tell("Au revoir.");

                }
            //case SessionEndedRequest sessionEndedRequest:
            //    return ResponseBuilder.Tell("La fonction est appelée correctement.");


        }

        return ResponseBuilder.Tell("Au revoir.");
    }

    private async Task<SkillResponse> GetTodo(ILambdaContext context, IntentRequest intentRequest)
    {
        var todoResult = await TaskSelector.GetTaskFor(new Member(CurrentSession.FirstName));
        if (!todoResult.TryGetValue(out var todo))
            ResponseBuilder.Tell("Il y a eu un problème pour récupérer une tâche à faire. Tu pourras retenter un peu plus tard.");

        var prompt = $"Tu peux {todo!.name} pour {todo.points} centimes. Acceptes-tu ou souhaites-tu une autre tâche ?";
        var response = ResponseBuilder.Ask(prompt, new Reprompt() { OutputSpeech = new Reprompt(prompt).OutputSpeech });

        return response;
    }

    private async Task<SkillResponse> GetBalance(ILambdaContext context, IntentRequest intentRequest)
    {
        var balanceResult = await TaskSelector.GetMemberBalance(new Member(CurrentSession.FirstName));
        if (!balanceResult.TryGetValue(out var balance))
            ResponseBuilder.Tell("Il y a eu un problème pour récupérer ton solde d'argent de poche. Tu pourras retenter un peu plus tard.");

        var prompt = AmountHelper.GetAmountToPromptText(balance!.amount);
        var response = ResponseBuilder.Ask(prompt, new Reprompt() { OutputSpeech = new Reprompt(prompt).OutputSpeech });
        return response;
    }

    private SkillResponse SetFirstName(ILambdaContext context, IntentRequest intentRequest)
    {
        string firstName = GetFirstNameFromSlot(intentRequest);

        CurrentSession.FirstName = firstName;

        var prompt = $"Bonjour {firstName}. Veux-tu une tâche à faire ou connaître ton solde d'argent de poche ?";
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
