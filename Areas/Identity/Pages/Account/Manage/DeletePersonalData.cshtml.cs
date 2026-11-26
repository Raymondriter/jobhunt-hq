using System.ComponentModel.DataAnnotations;
using JobHuntHQ.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JobHuntHQ.Areas.Identity.Pages.Account.Manage;

/// <summary>
/// Scaffolded replacement for the Identity default-UI delete-account page.
/// The default page deletes only the Identity user — but every JobHunt HQ
/// table keys on UserId with no foreign key, so the account's applications,
/// resume files, stories, tokens, and profile would be orphaned. This page
/// runs <see cref="UserDataPurgeService"/> first, then deletes the user,
/// mirroring the default page's flow (password check, sign-out, redirect).
/// </summary>
public class DeletePersonalDataModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly UserDataPurgeService _purge;
    private readonly ILogger<DeletePersonalDataModel> _logger;

    public DeletePersonalDataModel(
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager,
        UserDataPurgeService purge,
        ILogger<DeletePersonalDataModel> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _purge = purge;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;
    }

    public bool RequirePassword { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
        }

        RequirePassword = await _userManager.HasPasswordAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
        }

        RequirePassword = await _userManager.HasPasswordAsync(user);
        if (RequirePassword)
        {
            if (!await _userManager.CheckPasswordAsync(user, Input.Password))
            {
                ModelState.AddModelError(string.Empty, "Incorrect password.");
                return Page();
            }
        }

        var userId = await _userManager.GetUserIdAsync(user);

        // Purge ALL app data (resume blobs + every UserId-keyed row) BEFORE the
        // Identity user is deleted — afterwards the rows would be unreachable
        // orphans. Same purge as Profile/DeleteAllMyData.
        await _purge.PurgeUserDataAsync(userId);

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("An unexpected error occurred deleting user.");
        }

        await _signInManager.SignOutAsync();

        _logger.LogInformation("User with ID '{UserId}' deleted themselves (app data purged first).", userId);

        return Redirect("~/");
    }
}
