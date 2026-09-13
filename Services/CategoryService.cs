using Arctrix.PersonalMoneyTracker.Data;
using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.Services;

public interface ICategoryService
{
    Task<List<Category>> GetAllAsync(bool includeArchived = false);
    Task<Category?> GetByIdAsync(int id);
    Task<int> SaveAsync(Category category);
    Task ArchiveAsync(int categoryId);
}

public class CategoryService : ICategoryService
{
    private readonly AppDbContext _db;

    public CategoryService(AppDbContext db) => _db = db;

    public async Task<List<Category>> GetAllAsync(bool includeArchived = false)
    {
        await _db.InitializeAsync();
        var all = await _db.Connection.Table<Category>().ToListAsync();
        return includeArchived ? all : all.Where(c => !c.IsArchived).ToList();
    }

    public async Task<Category?> GetByIdAsync(int id)
    {
        await _db.InitializeAsync();
        return await _db.Connection.Table<Category>().FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<int> SaveAsync(Category category)
    {
        await _db.InitializeAsync();
        if (category.Id == 0)
            await _db.Connection.InsertAsync(category);
        else
            await _db.Connection.UpdateAsync(category);
        return category.Id;
    }

    public async Task ArchiveAsync(int categoryId)
    {
        var category = await GetByIdAsync(categoryId);
        if (category is null || category.IsSystem) return;
        category.IsArchived = true;
        await _db.Connection.UpdateAsync(category);
    }
}
