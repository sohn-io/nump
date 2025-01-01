
using Microsoft.EntityFrameworkCore;
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
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {

            // Ensure the item is tracked properly and does not conflict
            if (item.Guid == Guid.Empty)
            {
                item.Guid = Guid.NewGuid();
            }

            var existingItem = _context.Set<T>().FirstOrDefault(e => e.Guid == item.Guid);

            if (existingItem != null)
            {
                _context.Entry(existingItem).State = EntityState.Detached;
                _context.Update(item);
            }
            else
            {
                _context.Add<T>(item);
            }
            await _context.SaveChangesAsync();
            await transaction.CommitAsync(); // Commit transaction
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(); // Rollback if an error occurs
            throw;
        }
    }
}