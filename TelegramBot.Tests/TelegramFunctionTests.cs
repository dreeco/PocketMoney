using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.TestUtilities;
using Application.Budget;
using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Net;
using Xunit;

namespace TelegramBot.Tests;

public class TelegramFunctionMockedTests
{
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<IUserRequestHandler> _userRequestHandlerMock;

    public TelegramFunctionMockedTests()
    {
        var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.local.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

        foreach (var pair in configuration.AsEnumerable())
        {
            if (!string.IsNullOrEmpty(pair.Value))
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }


        _userRequestHandlerMock = new Mock<IUserRequestHandler>();
        _serviceProviderMock = new Mock<IServiceProvider>();

        // Injection de IUserRequestHandler dans le ServiceProvider
        _serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IUserRequestHandler)))
            .Returns(_userRequestHandlerMock.Object);
    }

    private TelegramFunction CreateSut()
    {
        return new TelegramFunction(_serviceProviderMock.Object);
    }

    [Fact]
    public async Task FunctionHandler_ShouldReturnBadRequest_WhenBodyIsEmpty()
    {
        // Arrange
        var sut = CreateSut();
        var request = new APIGatewayHttpApiV2ProxyRequest { Body = "" };
        var context = new TestLambdaContext();

        // Act
        var response = await sut.FunctionHandler(request, context);

        // Assert
        response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        response.Body.Should().Be("Empty body");
    }

    [Fact]
    public async Task FunctionHandler_ShouldReturnBadRequest_WhenJsonIsInvalid()
    {
        // Arrange
        var sut = CreateSut();
        var request = new APIGatewayHttpApiV2ProxyRequest { Body = "{ invalid_json " };
        var context = new TestLambdaContext();

        // Act
        var response = await sut.FunctionHandler(request, context);

        // Assert
        response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        response.Body.Should().Be("Invalid JSON");
    }

    [Fact]
    public async Task FunctionHandler_ShouldReturnForbidden_WhenUserIsNotAllowed()
    {
        // Arrange
        var sut = CreateSut();
        long unauthorizedUserId = 12321213132;

        // Simule un payload JSON Telegram d'un utilisateur non autorisé
        var telegramJson = $$"""
        {
          "update_id": 10000,
          "message": {
            "message_id": 1,
            "from": { "id": {{unauthorizedUserId}}, "is_bot": false, "first_name": "Hack" },
            "chat": { "id": {{unauthorizedUserId}}, "type": "private" },
            "date": 1441645532,
            "text": "10 euros café"
          }
        }
        """;

        var request = new APIGatewayHttpApiV2ProxyRequest { Body = telegramJson };
        var context = new TestLambdaContext();

        // Act
        var response = await sut.FunctionHandler(request, context);

        // Assert
        response.StatusCode.Should().Be((int)HttpStatusCode.Forbidden);
        response.Body.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task FunctionHandler_ShouldReturnNoContent_WhenMessageIsValidAndAuthorized()
    {
        // Arrange
        var sut = CreateSut();
        long authorizedUserId = 8818144478;

        var telegramJson = $$"""
        {
          "update_id": 10000,
          "message": {
            "message_id": 1,
            "from": { "id": {{authorizedUserId}}, "is_bot": false, "first_name": "Alex" },
            "chat": { "id": {{authorizedUserId}}, "type": "private" },
            "date": 1441645532,
            "text": "12 euros Gifi"
          }
        }
        """;

        _userRequestHandlerMock
            .Setup(x => x.ParseMessage(
                "12 euros Gifi",
                authorizedUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success()); // Adapte selon ton objet Result

        var request = new APIGatewayHttpApiV2ProxyRequest { Body = telegramJson };
        var context = new TestLambdaContext
        {
            // Simule un temps restant de 5 secondes dans Lambda
            RemainingTime = TimeSpan.FromSeconds(5)
        };

        // Act
        var response = await sut.FunctionHandler(request, context);

        // Assert
        response.StatusCode.Should().Be((int)HttpStatusCode.NoContent);

        // Vérifie que ton service métier a bien été appelé 1 seule fois avec les bonnes valeurs
        _userRequestHandlerMock.Verify(x => x.ParseMessage(
            "12 euros Gifi",
            authorizedUserId,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}