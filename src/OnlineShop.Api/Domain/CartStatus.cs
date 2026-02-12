namespace OnlineShop.Api.Domain;

public enum CartStatus
{
    Active = 0,
    CheckoutPending = 1,
    Completed = 2,
    Abandoned = 3,
    Merged = 4
}

