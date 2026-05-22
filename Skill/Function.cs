using Alexa.NET;
using Alexa.NET.Request;
using Alexa.NET.Response;
using Amazon.Lambda.Core;

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

    internal Function(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;

    }

    /// <summary>
    /// A simple function that takes a string and does a ToUpper
    /// </summary>
    /// <param name="input">The event for the Lambda function handler to process.</param>
    /// <param name="context">The ILambdaContext that provides methods for logging and describing the Lambda environment.</param>
    /// <returns></returns>
    public async Task<SkillResponse> FunctionHandler(SkillRequest? input, ILambdaContext? context)
    {
        return ResponseBuilder.Tell("La fonction est appelée correctement.");
    }
}
