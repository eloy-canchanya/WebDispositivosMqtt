using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebDispositivosMqtt.Data;
using WebDispositivosMqtt.Data.Models;

namespace WebDispositivosMqtt.Controllers.Api;

[ApiController]
[Route("api/alarms")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class AlarmsController(DatabaseContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 100) pageSize = 20;

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var alarms = await db.UserDevices
            .Where(ud => ud.UserId == userId)
            .SelectMany(ud => ud.Device.Alarms)
            .Where(a => !a.AlarmReads.Any(r => r.UserId == userId && r.DismissedAtUtc != null))
            .OrderByDescending(a => a.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AlarmDto(
                a.Id,
                a.DeviceId,
                a.Device.Name,
                a.Type,
                a.Description,
                a.Severity,
                a.CreatedAtUtc,
                a.AlarmReads.Any(r => r.UserId == userId)
            ))
            .ToListAsync();

        return Ok(alarms);
    }

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var hasAccess = await db.UserDevices
            .AnyAsync(ud => ud.UserId == userId && ud.Device.Alarms.Any(a => a.Id == id));

        if (!hasAccess)
            return NotFound();

        var existing = await db.AlarmReads
            .FirstOrDefaultAsync(r => r.AlarmId == id && r.UserId == userId);

        if (existing is null)
        {
            db.AlarmReads.Add(new AlarmRead { AlarmId = id, UserId = userId });
            await db.SaveChangesAsync();
        }

        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Dismiss(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var hasAccess = await db.UserDevices
            .AnyAsync(ud => ud.UserId == userId && ud.Device.Alarms.Any(a => a.Id == id));

        if (!hasAccess)
            return NotFound();

        var existing = await db.AlarmReads
            .FirstOrDefaultAsync(r => r.AlarmId == id && r.UserId == userId);

        if (existing is null)
        {
            db.AlarmReads.Add(new AlarmRead { AlarmId = id, UserId = userId, DismissedAtUtc = DateTime.UtcNow });
        }
        else
        {
            existing.DismissedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete]
    public async Task<IActionResult> DismissAll()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var deviceIds = await db.UserDevices
            .Where(ud => ud.UserId == userId)
            .Select(ud => ud.DeviceId)
            .ToListAsync();

        var alarmIds = await db.Alarms
            .Where(a => deviceIds.Contains(a.DeviceId))
            .Select(a => a.Id)
            .ToListAsync();

        var existing = await db.AlarmReads
            .Where(r => r.UserId == userId && alarmIds.Contains(r.AlarmId))
            .ToListAsync();

        var existingAlarmIds = existing.Select(r => r.AlarmId).ToHashSet();

        foreach (var read in existing)
            read.DismissedAtUtc = DateTime.UtcNow;

        var toAdd = alarmIds
            .Where(aid => !existingAlarmIds.Contains(aid))
            .Select(aid => new AlarmRead { AlarmId = aid, UserId = userId, DismissedAtUtc = DateTime.UtcNow });

        db.AlarmReads.AddRange(toAdd);
        await db.SaveChangesAsync();
        return Ok();
    }
}

public record AlarmDto(
    int Id,
    Guid DeviceId,
    string DeviceName,
    string Type,
    string Description,
    byte Severity,
    DateTime CreatedAtUtc,
    bool IsRead
);
