
using nump.Components.Classes;
using nump.Components.Database;

namespace nump.Components.Services;
public partial class SaveService<T> where T : class, IHasGuid
{
    protected readonly NumpContext _context;

    public SaveService(NumpContext _Context)
    {
        _context = _Context;
    }
    public async Task HandleSave(T item)
    {
        // Check if the item exists by its guid and either update or add it
        if (_context.Set<T>().Any(e => e.Guid == item.Guid))
        {
            _context.Update(item);
        }
        else
        {
            await _context.AddAsync(item);
        }

        await _context.SaveChangesAsync();
    }
}