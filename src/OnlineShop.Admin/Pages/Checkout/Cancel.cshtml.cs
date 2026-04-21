using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OnlineShop.Admin.Pages.Checkout;

public class CancelModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid? OrderId { get; set; }

    public void OnGet()
    {
    }
}