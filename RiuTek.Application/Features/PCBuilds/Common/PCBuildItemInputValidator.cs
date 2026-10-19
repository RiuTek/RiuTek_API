using FluentValidation;
using RiuTek.Application.DTOs;

namespace RiuTek.Application.Features.PCBuilds.Common;

public class PCBuildItemInputValidator : AbstractValidator<PCBuildItemInputDto>
{
    public PCBuildItemInputValidator()
    {
        RuleFor(i => i.ProductId).NotEmpty().WithMessage("Id sản phẩm không hợp lệ.");
        RuleFor(i => i.Quantity).InclusiveBetween(1, 20).WithMessage("Số lượng linh kiện phải từ 1 đến 20.");
    }
}
