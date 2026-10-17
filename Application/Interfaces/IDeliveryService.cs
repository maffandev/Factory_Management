using Application.DTOs.Workflow;

namespace Application.Interfaces;

public interface IDeliveryService
{
    Task<bool> ConfirmDeliveryAsync(DeliveryConfirmDto dto, int updatedByUserId);
}
