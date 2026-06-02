using BlazorHero.CleanArchitecture.Application.Features.Documents.Commands.AddEdit;
using BlazorHero.CleanArchitecture.Application.Features.Documents.Commands.Reject;
using BlazorHero.CleanArchitecture.Application.Features.Documents.Queries.GetAll;
using BlazorHero.CleanArchitecture.Application.Requests.Documents;
using BlazorHero.CleanArchitecture.Client.Extensions;
using MudBlazor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BlazorHero.CleanArchitecture.Client.Infrastructure.Managers.Misc.Document;
using BlazorHero.CleanArchitecture.Domain.Entities.Misc;
using BlazorHero.CleanArchitecture.Domain.Enums;
using BlazorHero.CleanArchitecture.Shared.Constants.Application;
using BlazorHero.CleanArchitecture.Shared.Constants.Permission;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace BlazorHero.CleanArchitecture.Client.Pages.Misc
{
    public partial class DocumentStore
    {
        [Inject] private IDocumentManager DocumentManager { get; set; }

        private IEnumerable<GetAllDocumentsResponse> _pagedData;
        private MudTable<GetAllDocumentsResponse> _table;
        private string CurrentUserId { get; set; }
        private int _totalItems;
        private int _currentPage;
        private string _searchString = "";
        private DocumentStatus? _statusFilter;
        private bool _dense = false;
        private bool _striped = true;
        private bool _bordered = false;

        private ClaimsPrincipal _currentUser;
        private bool _canCreateDocuments;
        private bool _canEditDocuments;
        private bool _canDeleteDocuments;
        private bool _canSearchDocuments;
        private bool _canViewDocumentExtendedAttributes;
        private bool _canApproveDocuments;
        private bool _canRejectDocuments;
        private bool _canArchiveDocuments;
        private bool _loaded;

        private HubConnection _hubConnection;

        protected override async Task OnInitializedAsync()
        {
            _currentUser = await _authenticationManager.CurrentUser();
            _canCreateDocuments = (await _authorizationService.AuthorizeAsync(_currentUser, Permissions.Documents.Create)).Succeeded;
            _canEditDocuments = (await _authorizationService.AuthorizeAsync(_currentUser, Permissions.Documents.Edit)).Succeeded;
            _canDeleteDocuments = (await _authorizationService.AuthorizeAsync(_currentUser, Permissions.Documents.Delete)).Succeeded;
            _canSearchDocuments = (await _authorizationService.AuthorizeAsync(_currentUser, Permissions.Documents.Search)).Succeeded;
            _canViewDocumentExtendedAttributes = (await _authorizationService.AuthorizeAsync(_currentUser, Permissions.DocumentExtendedAttributes.View)).Succeeded;
            _canApproveDocuments = (await _authorizationService.AuthorizeAsync(_currentUser, Permissions.Documents.Approve)).Succeeded;
            _canRejectDocuments = (await _authorizationService.AuthorizeAsync(_currentUser, Permissions.Documents.Reject)).Succeeded;
            _canArchiveDocuments = (await _authorizationService.AuthorizeAsync(_currentUser, Permissions.Documents.Archive)).Succeeded;

            _loaded = true;

            var state = await _stateProvider.GetAuthenticationStateAsync();
            var user = state.User;
            if (user == null) return;
            if (user.Identity?.IsAuthenticated == true)
            {
                CurrentUserId = user.GetUserId();
            }

            _hubConnection = _hubConnection.TryInitialize(_navigationManager, _localStorage);
            _hubConnection.On<string, string>(ApplicationConstants.SignalR.ReceiveDocumentApprovalNotification, (documentTitle, status) =>
            {
                _snackBar.Add($"{documentTitle} - {status}", Severity.Info);
                InvokeAsync(async () =>
                {
                    await LoadData(_currentPage, 10, new TableState { Page = _currentPage, PageSize = 10 });
                    StateHasChanged();
                });
            });
            await _hubConnection.StartAsync();
        }

        private (Color color, string icon) GetStatusDisplay(DocumentStatus status)
        {
            return status switch
            {
                DocumentStatus.Draft => (Color.Default, Icons.Material.Filled.Edit),
                DocumentStatus.PendingReview => (Color.Warning, Icons.Material.Filled.HourglassTop),
                DocumentStatus.Published => (Color.Success, Icons.Material.Filled.CheckCircle),
                DocumentStatus.Rejected => (Color.Error, Icons.Material.Filled.Cancel),
                DocumentStatus.Archived => (Color.Info, Icons.Material.Filled.Archive),
                _ => (Color.Default, Icons.Material.Filled.Help)
            };
        }

        private static string TruncateReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return string.Empty;
            return reason.Length > 30 ? $"{reason.Substring(0, 30)}..." : reason;
        }

        private async Task<TableData<GetAllDocumentsResponse>> ServerReload(TableState state)
        {
            if (!string.IsNullOrWhiteSpace(_searchString))
            {
                state.Page = 0;
            }
            await LoadData(state.Page, state.PageSize, state);
            return new TableData<GetAllDocumentsResponse> { TotalItems = _totalItems, Items = _pagedData };
        }

        private async Task LoadData(int pageNumber, int pageSize, TableState state)
        {
            var request = new GetAllPagedDocumentsRequest { PageSize = pageSize, PageNumber = pageNumber + 1, SearchString = _searchString, StatusFilter = _statusFilter };
            var response = await DocumentManager.GetAllAsync(request);
            if (response.Succeeded)
            {
                _totalItems = response.TotalCount;
                _currentPage = response.CurrentPage;
                var data = response.Data;
                var loadedData = data.Where(element =>
                {
                    if (string.IsNullOrWhiteSpace(_searchString))
                        return true;
                    if (element.Title.Contains(_searchString, StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (element.Description.Contains(_searchString, StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (element.DocumentType.Contains(_searchString, StringComparison.OrdinalIgnoreCase))
                        return true;
                    return false;
                });
                switch (state.SortLabel)
                {
                    case "documentIdField":
                        loadedData = loadedData.OrderByDirection(state.SortDirection, d => d.Id);
                        break;
                    case "documentTitleField":
                        loadedData = loadedData.OrderByDirection(state.SortDirection, d => d.Title);
                        break;
                    case "documentDescriptionField":
                        loadedData = loadedData.OrderByDirection(state.SortDirection, d => d.Description);
                        break;
                    case "documentDocumentTypeField":
                        loadedData = loadedData.OrderByDirection(state.SortDirection, p => p.DocumentType);
                        break;
                    case "documentIsPublicField":
                        loadedData = loadedData.OrderByDirection(state.SortDirection, d => d.IsPublic);
                        break;
                    case "documentStatusField":
                        loadedData = loadedData.OrderByDirection(state.SortDirection, d => d.Status);
                        break;
                    case "documentDateCreatedField":
                        loadedData = loadedData.OrderByDirection(state.SortDirection, d => d.CreatedOn);
                        break;
                    case "documentOwnerField":
                        loadedData = loadedData.OrderByDirection(state.SortDirection, d => d.CreatedBy);
                        break;
                }
                data = loadedData.ToList();
                _pagedData = data;
            }
            else
            {
                foreach (var message in response.Messages)
                {
                    _snackBar.Add(message, Severity.Error);
                }
            }
        }

        private void OnSearch(string text)
        {
            _searchString = text;
            _table.ReloadServerData();
        }

        private void OnStatusFilterChanged(DocumentStatus? status)
        {
            _statusFilter = status;
            _table.ReloadServerData();
        }

        private async Task InvokeModal(int id = 0)
        {
            var parameters = new DialogParameters();
            if (id != 0)
            {
                var doc = _pagedData.FirstOrDefault(c => c.Id == id);
                if (doc != null)
                {
                    parameters.Add(nameof(AddEditDocumentModal.AddEditDocumentModel), new AddEditDocumentCommand
                    {
                        Id = doc.Id,
                        Title = doc.Title,
                        Description = doc.Description,
                        URL = doc.URL,
                        IsPublic = doc.IsPublic,
                        DocumentTypeId = doc.DocumentTypeId
                    });
                }
            }
            var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Medium, FullWidth = true, DisableBackdropClick = true };
            var dialog = _dialogService.Show<AddEditDocumentModal>(id == 0 ? _localizer["Create"] : _localizer["Edit"], parameters, options);
            var result = await dialog.Result;
            if (!result.Cancelled)
            {
                OnSearch("");
            }
        }

        private async Task Delete(int id)
        {
            string deleteContent = _localizer["Delete Content"];
            var parameters = new DialogParameters
            {
                {nameof(Shared.Dialogs.DeleteConfirmation.ContentText), string.Format(deleteContent, id)}
            };
            var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small, FullWidth = true, DisableBackdropClick = true };
            var dialog = _dialogService.Show<Shared.Dialogs.DeleteConfirmation>(_localizer["Delete"], parameters, options);
            var result = await dialog.Result;
            if (!result.Cancelled)
            {
                var response = await DocumentManager.DeleteAsync(id);
                if (response.Succeeded)
                {
                    OnSearch("");
                    _snackBar.Add(response.Messages[0], Severity.Success);
                }
                else
                {
                    OnSearch("");
                    foreach (var message in response.Messages)
                    {
                        _snackBar.Add(message, Severity.Error);
                    }
                }
            }
        }

        private async Task SubmitForReview(int id)
        {
            var response = await DocumentManager.SubmitForReviewAsync(id);
            if (response.Succeeded)
            {
                OnSearch("");
                _snackBar.Add(response.Messages[0], Severity.Success);
            }
            else
            {
                foreach (var message in response.Messages)
                {
                    _snackBar.Add(message, Severity.Error);
                }
            }
        }

        private async Task Approve(int id)
        {
            var response = await DocumentManager.ApproveAsync(id);
            if (response.Succeeded)
            {
                OnSearch("");
                _snackBar.Add(response.Messages[0], Severity.Success);
            }
            else
            {
                foreach (var message in response.Messages)
                {
                    _snackBar.Add(message, Severity.Error);
                }
            }
        }

        private async Task OpenRejectDialog(int id)
        {
            var parameters = new DialogParameters
            {
                { nameof(RejectDocumentDialog.DocumentId), id }
            };
            var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small, FullWidth = true, DisableBackdropClick = true };
            var dialog = _dialogService.Show<RejectDocumentDialog>(_localizer["Reject Document"], parameters, options);
            var result = await dialog.Result;
            if (!result.Cancelled)
            {
                OnSearch("");
            }
        }

        private async Task Archive(int id)
        {
            var response = await DocumentManager.ArchiveAsync(id);
            if (response.Succeeded)
            {
                OnSearch("");
                _snackBar.Add(response.Messages[0], Severity.Success);
            }
            else
            {
                foreach (var message in response.Messages)
                {
                    _snackBar.Add(message, Severity.Error);
                }
            }
        }

        private void ManageExtendedAttributes(int documentId)
        {
            _navigationManager.NavigateTo($"/extended-attributes/{typeof(Document).Name}/{documentId}");
        }
    }
}