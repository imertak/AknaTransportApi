namespace AknaTransportApi.Domain.Common;

/// <summary>Para hesaplamaları için ortak kurallar. Tüm tutarlar decimal tutulur.</summary>
public static class Money
{
    public const string DefaultCurrency = "TRY";

    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal CalculateVat(decimal subTotal, decimal vatRate) => Round(subTotal * vatRate / 100m);
}
