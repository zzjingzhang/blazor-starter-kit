namespace BlazorHero.CleanArchitecture.Application.Enums
{
    public enum AuditType : byte
    {
        None = 0,
        Create = 1,
        Update = 2,
        Delete = 3,
        SubmitForReview = 4,
        Approve = 5,
        Reject = 6,
        Archive = 7
    }
}