using GTiff2Tiles.Server.Data;
using GTiff2Tiles.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GTiff2Tiles.Server.Pages.Admin.StoragePresets;

[Authorize]
public sealed class IndexModel(ServerDbContext dbContext) : PageModel
{
    public IReadOnlyList<StoragePreset> Presets { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Presets = await dbContext.StoragePresets
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        StoragePreset? preset = await dbContext.StoragePresets
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (preset is null)
            return NotFound();

        dbContext.StoragePresets.Remove(preset);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new EmptyResult();
    }
}
