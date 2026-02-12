namespace OnlineShop.Api.Domain
{
    public enum OrderStatus
    {
        PendingPayment = 0,
        Paid = 1,
        Cancelled = 2,
        Fulfilled = 3,
        Refunded = 4
    }
}
