using Alexa.NET;
using Alexa.NET.Request;
using Alexa.NET.Request.Type;
using Alexa.NET.Response;
using Alexa.NET.Response.Directive;
using Amazon.Lambda.Core;
using Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;

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

    private ICleaningTasksRepository CleaningTasksRepository { get; set; }

    public Function(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;

        CleaningTasksRepository = serviceProvider.GetRequiredService<ICleaningTasksRepository>();
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
                        return GetTodo(context, intentRequest);
                    case "GetBalance":
                        return GetBalance(context, intentRequest);
                    default:
                        return ResponseBuilder.Tell("Au revoir.");

                }
            //case SessionEndedRequest sessionEndedRequest:
            //    return ResponseBuilder.Tell("La fonction est appelée correctement.");


        }

        return ResponseBuilder.Tell("Au revoir.");
    }

    private SkillResponse GetTodo(ILambdaContext context, IntentRequest intentRequest)
    {
        var prompt = $"Va nettoyer le caca !";
        var response = ResponseBuilder.Ask(prompt, new Reprompt() { OutputSpeech = new Reprompt(prompt).OutputSpeech });
        return response;
    }

    private SkillResponse GetBalance(ILambdaContext context, IntentRequest intentRequest)
    {
        var prompt = $"Tu dois un million d'euros à papa !";
        var response = ResponseBuilder.Ask(prompt, new Reprompt() { OutputSpeech = new Reprompt(prompt).OutputSpeech });
        return response;
    }

    private SkillResponse SetFirstName(ILambdaContext context, IntentRequest intentRequest)
    {
        var firstName = intentRequest.Intent.Slots.Single().Value.SlotValue.Value.Trim();
        firstName = char.ToUpper(firstName[0]) + firstName.Substring(1).ToLower();

        var prompt = $"Bonjour {firstName}. Veux-tu une tâche à faire ou connaître ton solde d'argent de poche ?";
        var response = ResponseBuilder.Ask(prompt, new Reprompt() { OutputSpeech = new Reprompt(prompt).OutputSpeech });
        return response;
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
                Slots = new Dictionary<string, Slot> { {"firstName", new Slot { Name = "firstName" } } }
            }
        });

        logger.LogInformation($"Expected Next intent is : SetFirstName");
    }
}
