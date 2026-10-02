using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using LadingSystem.Models;
using LadingSystem.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LadingSystem.Controllers;

public class HomeController : Controller
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Dashboard([FromServices] ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var uid = User.FindFirstValue("firebase_uid");
        if (string.IsNullOrWhiteSpace(uid)) return RedirectToAction("Index");
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.FirebaseUid == uid, cancellationToken);
        return user is null ? RedirectToAction("Index") : View(user);
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
