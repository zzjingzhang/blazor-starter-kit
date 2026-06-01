using System;
using BlazorHero.CleanArchitecture.Domain.Enums;

namespace BlazorHero.CleanArchitecture.Application.Features.Documents.Queries.GetAll
{
    public class GetAllDocumentsResponse
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public bool IsPublic { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public string URL { get; set; }
        public string DocumentType { get; set; }
        public int DocumentTypeId { get; set; }
        public DocumentStatus Status { get; set; }
        public string ReviewerId { get; set; }
        public string RejectionReason { get; set; }
        public DateTime? ReviewedOn { get; set; }
    }
}