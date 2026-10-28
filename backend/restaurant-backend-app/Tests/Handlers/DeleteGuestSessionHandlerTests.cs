using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Handlers.GuestSessions;
using APIHandler.Services.GuestSessions;
using AutoFixture.Xunit2;
using Moq;
using System.Collections.Generic;

namespace APIHandler.Tests.Handlers;

public class DeleteGuestSessionHandlerTests
{
    [Theory]
    [AutoData]
    public async Task HandleRequest_GuestSessionNotFound_Returns404BeforeValidation400(
        string userId,
        string groupReservationId,
        string guestSessionId)
    {
        var guestSessionService = new Mock<IGuestSessionService>();
        guestSessionService
            .Setup(service => service.DeleteGuestSessionAsync(userId, groupReservationId, guestSessionId))
            .ThrowsAsync(new GuestSessionNotFoundException("Guest session was not found."));
        var handler = new DeleteGuestSessionHandler(guestSessionService.Object);
        var logger = new Mock<ILambdaLogger>();
        var context = new Mock<ILambdaContext>();
        context.Setup(lambdaContext => lambdaContext.Logger).Returns(logger.Object);
        var request = new APIGatewayProxyRequest
        {
            PathParameters = new Dictionary<string, string>
            {
                ["groupReservationId"] = groupReservationId,
                ["guestSessionId"] = guestSessionId
            },
            RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
            {
                Authorizer = new APIGatewayCustomAuthorizerContext
                {
                    Claims = new Dictionary<string, string> { ["sub"] = userId }
                }
            }
        };

        var response = await handler.HandleRequest(request, context.Object);

        Assert.Equal(404, response.StatusCode);
    }

    [Theory]
    [AutoData]
    public async Task HandleRequest_SessionValidationFailure_Returns400(
        string userId,
        string groupReservationId,
        string guestSessionId)
    {
        var guestSessionService = new Mock<IGuestSessionService>();
        guestSessionService
            .Setup(service => service.DeleteGuestSessionAsync(userId, groupReservationId, guestSessionId))
            .ThrowsAsync(new ValidationException("Guest session does not belong to this group reservation."));
        var handler = new DeleteGuestSessionHandler(guestSessionService.Object);
        var logger = new Mock<ILambdaLogger>();
        var context = new Mock<ILambdaContext>();
        context.Setup(lambdaContext => lambdaContext.Logger).Returns(logger.Object);
        var request = new APIGatewayProxyRequest
        {
            PathParameters = new Dictionary<string, string>
            {
                ["groupReservationId"] = groupReservationId,
                ["guestSessionId"] = guestSessionId
            },
            RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
            {
                Authorizer = new APIGatewayCustomAuthorizerContext
                {
                    Claims = new Dictionary<string, string> { ["sub"] = userId }
                }
            }
        };

        var response = await handler.HandleRequest(request, context.Object);

        Assert.Equal(400, response.StatusCode);
    }
}
