namespace AknaTransportApi.Business.Dtos.Integrations;

public sealed record SmsMessage(string PhoneNumber, string Text);
