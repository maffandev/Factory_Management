using Application.DTOs.Workflow;

namespace Application.Interfaces;

public interface IWorkflowService
{
    Task<List<ArticleDepartmentStatusDto>> GetByArticleAsync(int articleId);
    Task<ArticleDepartmentStatusDto?> GetStatusByIdAsync(int statusId);
    Task StartWorkAsync(StartDepartmentWorkDto dto, int updatedByUserId);
    Task EndWorkAsync(EndDepartmentWorkDto dto, int updatedByUserId);
    Task<List<ArticleDepartmentStatusDto>> GetPendingByDepartmentAsync(int departmentId, string? userCategory = null);
    Task UndoLastDepartmentAsync(int articleId, int updatedByUserId);
    Task UndoDeliverAsync(int articleId, int updatedByUserId);
    Task<List<ArticleDepartmentStatusDto>> GetSamplingAwaitingApprovalAsync(string? userCategory = null);
    Task ReviewSamplingAsync(int statusId, bool approved, string? note, int reviewedByUserId);
}
