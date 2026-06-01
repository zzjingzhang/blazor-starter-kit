namespace BlazorHero.CleanArchitecture.Client.Infrastructure.Routes
{
    public static class DocumentsEndpoints
    {
        public static string GetAllPaged(int pageNumber, int pageSize, string searchString, string statusFilter = "")
        {
            return $"api/documents?pageNumber={pageNumber}&pageSize={pageSize}&searchString={searchString}&statusFilter={statusFilter}";
        }

        public static string GetById(int documentId)
        {
            return $"api/documents/{documentId}";
        }

        public static string Save = "api/documents";
        public static string Delete = "api/documents";
        public static string SubmitForReview(int id) => $"api/documents/submit-for-review/{id}";
        public static string Approve(int id) => $"api/documents/approve/{id}";
        public static string Reject = "api/documents/reject";
        public static string Archive(int id) => $"api/documents/archive/{id}";
    }
}