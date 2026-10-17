using Application.DTOs.Fabric;
using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;

namespace Application.Services;

public class FabricService : IFabricService
{
    
    public const string DefaultFabricBy = "Factory Management";

    private readonly IFabricRepository _fabricRepository;

    public FabricService(IFabricRepository fabricRepository)
    {
        _fabricRepository = fabricRepository;
    }

    public async Task<List<FabricResponseDto>> GetAllAsync()
    {
        var fabrics = await _fabricRepository.GetAllActiveAsync();
        return fabrics.Select(MapToDto).ToList();
    }

    public async Task<FabricResponseDto?> GetByIdAsync(int id)
    {
        var fabric = await _fabricRepository.GetByIdAsync(id);
        return fabric == null ? null : MapToDto(fabric);
    }

    public async Task<FabricResponseDto> CreateAsync(FabricCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.FabricCode))
            throw new InvalidOperationException("Fabric code is required.");
        if (string.IsNullOrWhiteSpace(dto.Color))
            throw new InvalidOperationException("Fabric color is required.");
        var exists = await _fabricRepository.FabricVariantExistsAsync(dto.FabricCode.Trim(), dto.Color.Trim());
        if (exists)
            throw new InvalidOperationException("This fabric code and color already exist.");

        if (!Enum.TryParse<FabricUnit>(dto.Unit, true, out var unit))
            throw new InvalidOperationException("Unit must be Meter, Yard or Kg.");

        // Master info: when another colour already exists for this fabric code, the
        // Company / Type / Invoice # / Unit / Rate are copied from it so the whole code
        // stays consistent.
        // NOTE: FabricDate is deliberately NOT copied. Each colour has its own date
        // because deliveries arrive on different days, so the date is a per-row field.
        var master = await _fabricRepository.GetByCodeAsync(dto.FabricCode.Trim());
        if (master != null)
        {
            dto.FabricType = master.FabricType;
            dto.InvNum = master.InvNum;
            dto.Rate = master.Rate;
            dto.FabricBy = master.FabricBy;

            // Take the unit from the master too, otherwise colours of the same code
            // could end up mixing Meter and Kg.
            unit = master.Unit;
        }

        if (string.IsNullOrWhiteSpace(dto.FabricType))
            throw new InvalidOperationException("Fabric type is required.");

        var fabric = new Fabric
        {
            FabricCode = dto.FabricCode.Trim(),
            InvNum = dto.InvNum,
            FabricDate = dto.FabricDate,
            FabricType = dto.FabricType.Trim(),
            Color = dto.Color.Trim(),
            FabricBy = string.IsNullOrWhiteSpace(dto.FabricBy) ? DefaultFabricBy : dto.FabricBy.Trim(),
            Quantity = dto.Quantity ?? 0,
            AvailableQuantity = dto.Quantity ?? 0,
            Unit = unit,
            Rate = dto.Rate ?? 0,
            TotalAmount = (dto.Quantity ?? 0) * (dto.Rate ?? 0),
            Status = (dto.Quantity ?? 0) > 0 ? FabricStatus.InStock : FabricStatus.OutOfStock,
            IsActive = true
        };

        await _fabricRepository.AddAsync(fabric);
        await _fabricRepository.SaveChangesAsync();

        return MapToDto(fabric);
    }

    /// <summary>
    /// Edits a fabric variant.
    ///   Master fields (Company / Type / Invoice # / Unit / Rate)  -> applied to all colors of this FabricCode.
    ///   Per-row fields  (FabricDate / Quantity)                   -> applied only to this specific color.
    /// Quantity has a guard: new Quantity cannot be less than what has already
    /// been used in articles (for waste/sale, reduce Quantity yourself,
    /// AvailableQuantity will automatically adjust).
    /// </summary>
    public async Task<FabricResponseDto> UpdateAsync(FabricUpdateDto dto)
    {
        var fabric = await _fabricRepository.GetByIdAsync(dto.Id);
        if (fabric == null)
            throw new InvalidOperationException("Fabric not found.");

        if (!Enum.TryParse<FabricUnit>(dto.Unit, true, out var unit))
            throw new InvalidOperationException("Unit must be Meter, Yard or Kg.");

        if (string.IsNullOrWhiteSpace(dto.FabricType))
            throw new InvalidOperationException("Fabric type is required.");

        // Quantity already consumed by articles. This can never be given back.
        var used = fabric.Quantity - fabric.AvailableQuantity;
        if (used < 0) used = 0;

        var newQuantity = dto.Quantity ?? fabric.Quantity;
        if (newQuantity < used)
            throw new InvalidOperationException(
                $"Quantity cannot be lower than {used} {fabric.Unit}, because that much has already been used in articles. Remove it from those articles first.");

        // ---------------- Master info: applied to the whole FabricCode ----------------
        var variants = await _fabricRepository.GetVariantsByCodeAsync(fabric.FabricCode);
        var fabricBy = string.IsNullOrWhiteSpace(dto.FabricBy) ? DefaultFabricBy : dto.FabricBy.Trim();
        var fabricType = dto.FabricType.Trim();
        var rate = dto.Rate ?? fabric.Rate;

        foreach (var v in variants)
        {
            v.FabricBy = fabricBy;
            v.FabricType = fabricType;
            v.InvNum = dto.InvNum;
            v.Unit = unit;
            v.Rate = rate;
            _fabricRepository.Update(v);
        }

        // -------------- Per-row: Date and Quantity apply only to this color ---------------
        // Used amount remains constant; AvailableQuantity adjusts accordingly.
        // (For waste/sale, reduce Quantity manually — AvailableQuantity will automatically decrease)
        fabric.FabricDate = dto.FabricDate;
        fabric.Quantity = newQuantity;
        fabric.AvailableQuantity = newQuantity - used;
        if (fabric.AvailableQuantity < 0) fabric.AvailableQuantity = 0;

        // Recalculate TotalAmount and Status for every variant after a rate/quantity change.
        foreach (var v in variants)
        {
            v.TotalAmount = v.Quantity * v.Rate;
            v.Status = v.AvailableQuantity > 0 ? FabricStatus.InStock : FabricStatus.OutOfStock;
        }

        await _fabricRepository.SaveChangesAsync();

        return MapToDto(fabric);
    }

    public async Task TopUpAsync(FabricTopUpDto dto)
    {
        if (dto.AddedQuantity <= 0)
            throw new InvalidOperationException("Additional quantity must be greater than zero.");

        var fabric = await _fabricRepository.GetByIdAsync(dto.FabricId);
        if (fabric == null)
            throw new InvalidOperationException("Fabric not found.");

        fabric.Quantity += dto.AddedQuantity;
        fabric.AvailableQuantity += dto.AddedQuantity;

        if (dto.NewRate.HasValue)
            fabric.Rate = dto.NewRate.Value;

        fabric.TotalAmount = fabric.Quantity * fabric.Rate;
        fabric.Status = fabric.AvailableQuantity > 0 ? FabricStatus.InStock : FabricStatus.OutOfStock;

        _fabricRepository.Update(fabric);
        await _fabricRepository.SaveChangesAsync();
    }

    /// <summary>
    /// Records a manual "Fabric Out" — fabric physically taken out from stock.
    /// Purchase (Quantity) and TotalAmount stay fixed.
    /// AvailableQuantity decreases; OutQuantity increases (cumulative).
    /// Guard: OutQuantity cannot exceed current AvailableQuantity.
    /// </summary>
    public async Task FabricOutAsync(FabricOutDto dto)
    {
        var fabric = await _fabricRepository.GetByIdAsync(dto.FabricId);
        if (fabric == null)
            throw new InvalidOperationException("Fabric not found.");

        if (dto.OutQuantity <= 0)
            throw new InvalidOperationException("Out quantity must be greater than zero.");

        if (dto.OutQuantity > fabric.AvailableQuantity)
            throw new InvalidOperationException(
                $"Cannot take out {dto.OutQuantity} {fabric.Unit} — only {fabric.AvailableQuantity} {fabric.Unit} available.");

        fabric.OutQuantity += dto.OutQuantity;
        fabric.AvailableQuantity -= dto.OutQuantity;

        if (fabric.AvailableQuantity <= 0)
        {
            fabric.AvailableQuantity = 0;
            fabric.Status = FabricStatus.OutOfStock;
        }
        else
        {
            fabric.Status = FabricStatus.InStock;
        }

        _fabricRepository.Update(fabric);
        await _fabricRepository.SaveChangesAsync();
    }


    public async Task DeductForArticleAsync(int fabricId, decimal quantityUsed)
    {
        if (quantityUsed < 0)
            throw new InvalidOperationException("Fabric quantity used cannot be negative.");

        var fabric = await _fabricRepository.GetByIdAsync(fabricId);
        if (fabric == null)
            throw new InvalidOperationException("Fabric not found.");

        if (fabric.AvailableQuantity < quantityUsed)
            throw new InvalidOperationException($"Not enough stock for fabric '{fabric.FabricCode}'. Available: {fabric.AvailableQuantity}, Required: {quantityUsed}");

        fabric.AvailableQuantity -= quantityUsed;

        if (fabric.AvailableQuantity <= 0)
        {
            fabric.AvailableQuantity = 0;
            fabric.Status = FabricStatus.OutOfStock;
        }

        _fabricRepository.Update(fabric);
        await _fabricRepository.SaveChangesAsync();
    }

    public async Task ReturnForArticleAsync(int fabricId, decimal quantity)
    {
        var fabric = await _fabricRepository.GetByIdAsync(fabricId);
        if (fabric == null)
            throw new InvalidOperationException("Fabric not found.");

        fabric.AvailableQuantity += quantity;
        if (fabric.AvailableQuantity > 0)
            fabric.Status = FabricStatus.InStock;

        _fabricRepository.Update(fabric);
        await _fabricRepository.SaveChangesAsync();
    }

    private static FabricResponseDto MapToDto(Fabric f) => new()
    {
        Id = f.Id,
        FabricCode = f.FabricCode,
        InvNum = f.InvNum,
        FabricDate = f.FabricDate,
        FabricType = f.FabricType,
        Color = f.Color,
        FabricBy = f.FabricBy,
        Quantity = f.Quantity,
        AvailableQuantity = f.AvailableQuantity,
        OutQuantity = f.OutQuantity,
        Unit = f.Unit.ToString(),
        Rate = f.Rate,
        TotalAmount = f.TotalAmount,
        AvailableAmount = f.AvailableQuantity * f.Rate,
        Status = f.Status.ToString()
    };
}