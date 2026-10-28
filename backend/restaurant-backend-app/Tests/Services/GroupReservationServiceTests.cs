using APIHandler.Constants;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Models.GroupReservation;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.Auth;
using APIHandler.Services.GroupReservations;
using APIHandler.Services.Locations;
using AutoFixture.Xunit2;
using Moq;

namespace APIHandler.Tests.Services;

public class GroupReservationServiceTests
{
    private readonly Mock<IGroupReservationRepository> _groupReservationRepositoryMock = new();
    private readonly Mock<ITableRepository> _tableRepositoryMock = new();
    private readonly Mock<IReservationRepository> _reservationRepositoryMock = new();
    private readonly Mock<IGuestSessionRepository> _guestSessionRepositoryMock = new();
    private readonly Mock<IOrderRepository> _orderRepositoryMock = new();
    private readonly Mock<IUserService> _userServiceMock = new();
    private readonly Mock<ILocationService> _locationServiceMock = new();

    private readonly GroupReservationService _service;

    public GroupReservationServiceTests()
    {
        _service = new GroupReservationService(
            _groupReservationRepositoryMock.Object,
            _tableRepositoryMock.Object,
            _reservationRepositoryMock.Object,
            _guestSessionRepositoryMock.Object,
            _orderRepositoryMock.Object,
            _userServiceMock.Object,
            _locationServiceMock.Object);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationByTokenAsync_ValidInput_ReturnsGroupReservationInfo(
        GroupReservation groupReservation,
        Table table,
        Reservation reservation,
        Location location,
        string expectedUserName)
    {
        groupReservation.ReservationId = reservation.Id;
        groupReservation.Status = GroupReservationStatus.Open;
        reservation.TableId = table.Id;
        table.LocationId = location.Id;

        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByTokenAsync(groupReservation.ReservationToken))
            .ReturnsAsync(groupReservation);

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _guestSessionRepositoryMock
            .Setup(r => r.CountByGroupReservationId(groupReservation.GroupReservationId))
            .ReturnsAsync(0);

        _tableRepositoryMock
            .Setup(r => r.GetTableAsync(table.Id))
            .ReturnsAsync(table);

        _locationServiceMock
            .Setup(s => s.GetLocationByIdAsync(table.LocationId))
            .ReturnsAsync(location);

        _userServiceMock
            .Setup(s => s.GetUserName(reservation.CustomerId!))
            .ReturnsAsync(expectedUserName);

        var result = await _service.GetGroupReservationByTokenAsync(groupReservation.ReservationToken);

        Assert.Equal(groupReservation.GroupReservationId, result.GroupReservationId);
        Assert.Equal(table.LocationId, result.LocationId);
        Assert.Equal(reservation.GuestCount, result.MaxGuestCount);
        Assert.Equal(reservation.StartTime, result.StartTime);
        Assert.Equal(reservation.EndTime, result.EndTime);
        Assert.Equal(table.TableNumber, result.TableNumber);
        Assert.Equal("Green & Tasty", result.RestaurantName);
        Assert.Equal(location.Address, result.LocationAddress);
        Assert.Equal(expectedUserName, result.InviterName);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationByTokenAsync_InvalidToken_ThrowsNotFound(string token)
    {
        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByTokenAsync(token))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => _service.GetGroupReservationByTokenAsync(token));
    }

    [Theory]
    [InlineAutoData(GroupReservationStatus.Closed)]
    [InlineAutoData(GroupReservationStatus.Submitted)]
    public async Task GetGroupReservationByTokenAsync_GroupReservationIsNotOpen_ThrowsException(
        GroupReservationStatus status,
        GroupReservation groupReservation)
    {
        groupReservation.Status = status;

        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByTokenAsync(groupReservation.ReservationToken))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync<GroupReservationExpiredException>(() => _service.GetGroupReservationByTokenAsync(groupReservation.ReservationToken));
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationByTokenAsync_WhenGuestCountIsMax_ThrowsGuestLimitExceeded(GroupReservation groupReservation, Reservation reservation)
    {
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.ReservationId = reservation.Id;

        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByTokenAsync(groupReservation.ReservationToken))
            .ReturnsAsync(groupReservation);

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _guestSessionRepositoryMock
            .Setup(r => r.CountByGroupReservationId(groupReservation.GroupReservationId))
            .ReturnsAsync(reservation.GuestCount);

        await Assert.ThrowsAsync<GuestLimitExceededException>(() => _service.GetGroupReservationByTokenAsync(groupReservation.ReservationToken));
    }

    [Theory]
    [AutoData]
    public async Task CloseGroupReservationAsync_ValidInput_UpdatesGroupReservationStatus(GroupReservation groupReservation)
    {
        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(groupReservation.ReservationId))
            .ReturnsAsync(groupReservation);

        GroupReservation? savedGroupReservation = null;
        _groupReservationRepositoryMock
            .Setup(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>()))
            .Callback<GroupReservation>(r => savedGroupReservation = r);

        await _service.CloseGroupReservationAsync(groupReservation.ReservationId);

        Assert.NotNull(savedGroupReservation);
        Assert.Equal(GroupReservationStatus.Closed, savedGroupReservation.Status);
    }

    [Theory]
    [AutoData]
    public async Task CloseGroupReservationAsync_GroupReservationNotFound_DoesNotThrowException(string reservationId)
    {
        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(reservationId))
            .ReturnsAsync((GroupReservation?)null);

        await _service.CloseGroupReservationAsync(reservationId);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationInviteTokenAsync_ValidInput_ReturnsToken(Reservation reservation, GroupReservation groupReservation)
    {
        reservation.Status = ReservationStatus.Reserved;
        reservation.GuestCount = 3;
        groupReservation.ReservationId = reservation.Id;
        groupReservation.Status = GroupReservationStatus.Open;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(reservation.Id))
            .ReturnsAsync(groupReservation);

        GetGroupInviteTokenResponse result = await _service.GetGroupReservationInviteTokenAsync(reservation.Id);

        Assert.Equal(groupReservation.ReservationToken, result.Token);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationInviteTokenAsync_InvalidReservationId_ThrowsNotFound(string reservationId)
    {
        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservationId))
            .ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() => _service.GetGroupReservationInviteTokenAsync(reservationId));
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationInviteTokenAsync_OneGuest_ThrowsValidationException(Reservation reservation)
    {
        reservation.Status = ReservationStatus.Reserved;
        reservation.GuestCount = 1;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        await Assert.ThrowsAsync<ValidationException>(() => _service.GetGroupReservationInviteTokenAsync(reservation.Id));
    }

    [Theory]
    [InlineAutoData(ReservationStatus.Completed)]
    [InlineAutoData(ReservationStatus.Cancelled)]
    [InlineAutoData(ReservationStatus.InProgress)]
    public async Task GetGroupReservationInviteTokenAsync_StatusIsNotReserved_ThrowsValidationException(ReservationStatus reservationStatus, Reservation reservation)
    {
        reservation.Status = reservationStatus;
        reservation.GuestCount = 1;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        await Assert.ThrowsAsync<ValidationException>(() => _service.GetGroupReservationInviteTokenAsync(reservation.Id));
    }

    [Theory]
    [InlineAutoData(GroupReservationStatus.Closed, typeof(GroupReservationExpiredException))]
    [InlineAutoData(GroupReservationStatus.Submitted, typeof(GroupReservationSubmittedException))]
    public async Task GetGroupReservationInviteTokenAsync_GroupStatusIsNotOpen_ThrowsException(GroupReservationStatus status, Type type, Reservation reservation, GroupReservation groupReservation)
    {
        reservation.Status = ReservationStatus.Reserved;
        reservation.GuestCount = 3;
        groupReservation.ReservationId = reservation.Id;
        groupReservation.Status = status;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(reservation.Id))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync(type, () => _service.GetGroupReservationInviteTokenAsync(reservation.Id));
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_Owner_ConsolidatesOnlySubmittedSessionsAndTransactsBothStates(
        string userId,
        GroupReservation groupReservation,
        Reservation reservation,
        Order order,
        GuestSession firstSubmitted,
        GuestSession secondSubmitted,
        GuestSession unsubmitted)
    {
        groupReservation.ReservationId = reservation.Id;
        groupReservation.Status = GroupReservationStatus.Open;
        reservation.CustomerId = userId;
        order.ReservationId = reservation.Id;
        firstSubmitted.IsSubmitted = true;
        firstSubmitted.Dishes = [new OrderItem { DishId = "dish-1", Quantity = 2, State = OrderItemState.Pending }];
        secondSubmitted.IsSubmitted = true;
        secondSubmitted.Dishes =
        [
            new OrderItem { DishId = "dish-1", Quantity = 1, State = OrderItemState.Pending },
            new OrderItem { DishId = "dish-2", Quantity = 2, State = OrderItemState.Pending }
        ];
        unsubmitted.IsSubmitted = false;
        unsubmitted.Dishes = [new OrderItem { DishId = "ignored", Quantity = 9, State = OrderItemState.Pending }];

        _groupReservationRepositoryMock
            .Setup(repository => repository.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);
        _reservationRepositoryMock
            .Setup(repository => repository.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);
        _guestSessionRepositoryMock
            .Setup(repository => repository.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync([firstSubmitted, secondSubmitted, unsubmitted]);
        _orderRepositoryMock
            .Setup(repository => repository.GetOrderByReservationIdAsync(reservation.Id))
            .ReturnsAsync(order);
        _orderRepositoryMock
            .Setup(repository => repository.SaveGroupPreOrderTransactionAsync(order, groupReservation))
            .Returns(Task.CompletedTask);

        List<SubmitGroupPreOrderItemResponse> result = await _service.SubmitGroupPreOrderAsync(
            userId,
            groupReservation.GroupReservationId,
            reservation.Id);

        Assert.Collection(result,
            item =>
            {
                Assert.Equal("dish-1", item.DishId);
                Assert.Equal(3, item.Quantity);
            },
            item =>
            {
                Assert.Equal("dish-2", item.DishId);
                Assert.Equal(2, item.Quantity);
            });
        Assert.Equal(OrderStatus.SubmittedByCustomer, order.Status);
        Assert.Equal(GroupReservationStatus.Submitted, groupReservation.Status);
        Assert.Equal(2, order.Dishes.Count);
        Assert.DoesNotContain(order.Dishes, item => item.DishId == "ignored");
        _orderRepositoryMock.Verify(
            repository => repository.SaveGroupPreOrderTransactionAsync(order, groupReservation),
            Times.Once);
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_MissingGroup_ThrowsGroupReservationNotFoundException(
        string userId,
        string groupReservationId,
        string reservationId)
    {
        _groupReservationRepositoryMock
            .Setup(repository => repository.GetGroupReservationByIdAsync(groupReservationId))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupReservationId, reservationId));
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_MissingReservation_ThrowsReservationNotFoundException(
        string userId,
        GroupReservation groupReservation,
        string reservationId)
    {
        groupReservation.ReservationId = reservationId;
        _groupReservationRepositoryMock
            .Setup(repository => repository.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);
        _reservationRepositoryMock
            .Setup(repository => repository.GetReservationByIdAsync(reservationId))
            .ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservationId));
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_NonOwner_ThrowsActionForbiddenException(
        string ownerId,
        string otherUserId,
        GroupReservation groupReservation,
        Reservation reservation)
    {
        groupReservation.ReservationId = reservation.Id;
        reservation.CustomerId = ownerId;
        _groupReservationRepositoryMock
            .Setup(repository => repository.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);
        _reservationRepositoryMock
            .Setup(repository => repository.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        await Assert.ThrowsAsync<ActionForbiddenException>(() =>
            _service.SubmitGroupPreOrderAsync(otherUserId, groupReservation.GroupReservationId, reservation.Id));
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_NoSubmittedSessions_ThrowsValidationException(
        string userId,
        GroupReservation groupReservation,
        Reservation reservation,
        GuestSession session)
    {
        groupReservation.ReservationId = reservation.Id;
        reservation.CustomerId = userId;
        session.IsSubmitted = false;
        _groupReservationRepositoryMock
            .Setup(repository => repository.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);
        _reservationRepositoryMock
            .Setup(repository => repository.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);
        _guestSessionRepositoryMock
            .Setup(repository => repository.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync([session]);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id));
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_BodyReservationDoesNotMatchGroup_ThrowsValidationException(
        string userId,
        GroupReservation groupReservation,
        Reservation groupReservationOwner,
        Reservation otherReservation)
    {
        groupReservation.ReservationId = groupReservationOwner.Id;
        groupReservationOwner.CustomerId = userId;
        _groupReservationRepositoryMock
            .Setup(repository => repository.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);
        _reservationRepositoryMock
            .Setup(repository => repository.GetReservationByIdAsync(groupReservationOwner.Id))
            .ReturnsAsync(groupReservationOwner);
        _reservationRepositoryMock
            .Setup(repository => repository.GetReservationByIdAsync(otherReservation.Id))
            .ReturnsAsync(otherReservation);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, otherReservation.Id));
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_SubmittedSessionsHaveNoDishes_ThrowsValidationException(
        string userId,
        GroupReservation groupReservation,
        Reservation reservation,
        GuestSession session)
    {
        groupReservation.ReservationId = reservation.Id;
        reservation.CustomerId = userId;
        session.IsSubmitted = true;
        session.Dishes = [];
        _groupReservationRepositoryMock
            .Setup(repository => repository.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);
        _reservationRepositoryMock
            .Setup(repository => repository.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);
        _guestSessionRepositoryMock
            .Setup(repository => repository.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync([session]);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id));
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_MissingOrder_ThrowsValidationException(
        string userId,
        GroupReservation groupReservation,
        Reservation reservation,
        GuestSession session)
    {
        groupReservation.ReservationId = reservation.Id;
        reservation.CustomerId = userId;
        session.IsSubmitted = true;
        session.Dishes = [new OrderItem { DishId = "dish-1", Quantity = 1 }];
        _groupReservationRepositoryMock
            .Setup(repository => repository.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);
        _reservationRepositoryMock
            .Setup(repository => repository.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);
        _guestSessionRepositoryMock
            .Setup(repository => repository.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync([session]);
        _orderRepositoryMock
            .Setup(repository => repository.GetOrderByReservationIdAsync(reservation.Id))
            .ReturnsAsync((Order?)null);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id));
    }
}
