namespace CourtBook.Application.DTOs;

public class CourtAddonDto
{
    public Guid Id { get; set; }
    public Guid CourtId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public decimal Price { get; set; }
    public string Unit { get; set; } = "Item";
    public bool IsAvailable { get; set; }
}

public class CreateCourtAddonDto
{
    public Guid CourtId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public decimal Price { get; set; }
    public string Unit { get; set; } = "Item";
    public bool IsAvailable { get; set; } = true;
}

public class UpdateCourtAddonDto
{
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public decimal Price { get; set; }
    public string Unit { get; set; } = "Item";
    public bool IsAvailable { get; set; }
}

public class SelectedAddonDto
{
    public Guid CourtAddonId { get; set; }
    public int Quantity { get; set; } = 1;
}

public class BookingAddonDto
{
    public Guid Id { get; set; }
    public Guid CourtAddonId { get; set; }
    public string AddonName { get; set; } = string.Empty;
    public string? AddonNameAr { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
}
