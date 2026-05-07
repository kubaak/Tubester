namespace Tubester.Application.Common;

public class PaymentRequiredException : Exception
{
    public PaymentRequiredException() { }
    public PaymentRequiredException(string message) : base(message) { }
    public PaymentRequiredException(string message, Exception inner) : base(message, inner) { }
}