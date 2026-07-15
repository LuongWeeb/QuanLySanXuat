using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;

namespace WmsMes.Web.Repositories;

public class GenericRepository<T> : IGenericRepository<T>
    where T : class
{
    protected readonly ApplicationDbContext Context;
    private readonly DbSet<T> _dbSet;

    public GenericRepository(ApplicationDbContext context)
    {
        Context = context;
        _dbSet = context.Set<T>();
    }

    public async Task<IEnumerable<T>> GetAllAsync()
    {
        return await _dbSet.ToListAsync();
    }

    public async Task<T?> GetByIdAsync(int id)
    {
        return await _dbSet.FindAsync(id);
    }

    public async Task AddAsync(T entity)
    {
        await _dbSet.AddAsync(entity);
    }

    public void Update(T entity)
    {
        Context.Entry(entity).State = EntityState.Modified;
    }

    public void Delete(T entity)
    {
        _dbSet.Remove(entity);
    }

    public async Task SaveAsync()
    {
        await Context.SaveChangesAsync();
    }
}
