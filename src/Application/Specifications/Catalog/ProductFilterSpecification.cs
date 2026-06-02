using BlazorHero.CleanArchitecture.Application.Enums;
using BlazorHero.CleanArchitecture.Application.Extensions;
using BlazorHero.CleanArchitecture.Application.Specifications.Base;
using BlazorHero.CleanArchitecture.Domain.Entities.Catalog;
using System;

namespace BlazorHero.CleanArchitecture.Application.Specifications.Catalog
{
    public class ProductFilterSpecification : HeroSpecification<Product>
    {
        public ProductFilterSpecification(string searchString, ProductStatusFilter? statusFilter = null)
        {
            Includes.Add(a => a.Brand);
            if (!string.IsNullOrEmpty(searchString))
            {
                Criteria = p => p.Barcode != null && (p.Name.Contains(searchString) || p.Description.Contains(searchString) || p.Barcode.Contains(searchString) || p.Brand.Name.Contains(searchString));
            }
            else
            {
                Criteria = p => p.Barcode != null;
            }

            if (statusFilter.HasValue)
            {
                switch (statusFilter.Value)
                {
                    case ProductStatusFilter.Active:
                        Criteria = Criteria.And(p => p.IsActive);
                        break;
                    case ProductStatusFilter.Inactive:
                        Criteria = Criteria.And(p => !p.IsActive);
                        break;
                    case ProductStatusFilter.OutOfStock:
                        Criteria = Criteria.And(p => p.Stock == 0);
                        break;
                }
            }
        }
    }
}