using Alexa.NET.Request;
using Alexa.NET.Request.Type;
using Alexa.NET.Response;
using Amazon.Lambda.TestUtilities;
using Newtonsoft.Json;
using Xunit;

namespace Skill.Tests;

public class FunctionTest : BaseFunctionTest
{
    [Fact]
    public async Task TestToUpperFunction()
    {
        _request.Request = new LaunchRequest();
        await _sut.FunctionHandler(_request, _context);
    }
    
    [Theory]
    [InlineData("Base")]
    [InlineData("AnswerGetFirstName")]
    public async Task ShouldProvideExpectedResponse_WhenCallingFunction_WithSpecificRequest(string fileName)
    {
        var skillRequest = ReadRequestFile(fileName);

        var response = await _sut.FunctionHandler(skillRequest, new TestLambdaContext());

        var responseObject = ReadResponseFile(fileName);

        Assert.Equivalent(responseObject, response);
    }


    private static SkillResponse ReadResponseFile(string fileName)
    {
        return ReadSkillJsonFile<SkillResponse>("Responses", fileName);
    }

    private static SkillRequest ReadRequestFile(string fileName)
    {
        return ReadSkillJsonFile<SkillRequest>("Requests", fileName);
    }

    private static T ReadSkillJsonFile<T>(string subFolder, string fileName)
    {
        var text = File.ReadAllText($"./TestsJson/{subFolder}/{fileName}.json", System.Text.Encoding.UTF8);
        return JsonConvert.DeserializeObject<T>(text) ?? throw new Exception($"Impossible to deserialize {fileName}.json");

    }
}
