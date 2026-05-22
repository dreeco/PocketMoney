using Alexa.NET.Request;
using Alexa.NET.Request.Type;
using Alexa.NET.Response;
using Amazon.Lambda.TestUtilities;
using Xunit;

namespace Skill.Tests;


public class BaseFunctionTest
{
    protected TestLambdaContext _context;
    protected SkillRequest _request;
    protected Function _sut;

    public BaseFunctionTest()
    {
        _context = new TestLambdaContext() { ClientContext = new TestClientContext() { Custom = new Dictionary<string, string>() } };
        _request = new SkillRequest();
        _request.Session = new Session();
        //_request.Session.Attributes = new HomeworkSession();
        _sut = new Function();
    }

    protected static T ThenThereIsAnOutputSpeech<T>(SkillResponse response) where T : class, IOutputSpeech
    {
        var speech = response.Response.OutputSpeech as T ?? throw new Exception($"Expected response of type {typeof(T)} but got {response.Response.OutputSpeech.GetType()}");
        Assert.NotNull(speech);
        return speech;
    }

    protected void BuildSkillRequestWithIntent(string intentName)
    {
        _request.Request = new IntentRequest()
        {
            Intent = new Intent()
            {
                Name = intentName,
                Slots = new Dictionary<string, Slot>()
              .Where(s => !string.IsNullOrEmpty(s.Key))
              .ToDictionary(
                  s => s.Key,
                  s => new Slot() { Value = s.Value.ToString(), SlotValue = new SlotValue() { Value = s.Value.ToString() } }
              )
            }
        };
    }

    protected void BuildSkillLaunchRequest()
    {
        _request.Request = new LaunchRequest();
    }

}
