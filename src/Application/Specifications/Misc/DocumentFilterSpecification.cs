using BlazorHero.CleanArchitecture.Application.Specifications.Base;
using BlazorHero.CleanArchitecture.Domain.Entities.Misc;
using BlazorHero.CleanArchitecture.Domain.Enums;

namespace BlazorHero.CleanArchitecture.Application.Specifications.Misc
{
    public class DocumentFilterSpecification : HeroSpecification<Document>
    {
        public DocumentFilterSpecification(string searchString, string userId, DocumentStatus? statusFilter = null)
        {
            if (!string.IsNullOrEmpty(searchString))
            {
                Criteria = p => (p.Title.Contains(searchString) || p.Description.Contains(searchString)) && (p.IsPublic == true || (p.IsPublic == false && p.CreatedBy == userId));
            }
            else
            {
                Criteria = p => (p.IsPublic == true || (p.IsPublic == false && p.CreatedBy == userId));
            }

            if (statusFilter.HasValue)
            {
                And(p => p.Status == statusFilter.Value);
            }
        }
    }
}