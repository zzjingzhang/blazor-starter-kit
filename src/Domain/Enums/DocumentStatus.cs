namespace BlazorHero.CleanArchitecture.Domain.Enums
{
    public enum DocumentStatus : byte
    {
        Draft = 0,
        PendingReview = 1,
        Published = 2,
        Rejected = 3,
        Archived = 4
    }
}
