using IMCAPI.DB.Entities;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace IMCAPI.DB.Repositories
{

    /// <summary></summary><typeparam name="T">DataModel</typeparam>
    public interface IRepository<T> where T : Base
    {
        /// <summary>
        /// Gets all Entities in DB.
        /// </summary>
        /// <returns></returns>
        Task<List<T>> GetAll();
        /// <summary>
        /// Gets an Entity filtered by Id.
        /// </summary>
        /// <param name="id">Id of the desired entity.</param>
        /// <returns></returns>
        Task<T?> GetById(int id);
        /// <summary>
        /// Gets all entities that satisfy the condition.
        /// </summary>
        /// <param name="condition">Expression of the condition to satisfy.</param>
        /// <returns></returns>
        Task<List<T>> GetAllByCondition(Expression<Func<T, bool>> condition);

        /// <summary>
        /// Adds a new Entity to DB asynchronous
        /// </summary>
        /// <param name="entity">Entity to create.</param>
        /// <returns></returns>
        Task Add(T entity);
        /// <summary>
        /// Adds many Entities to DB asynchronous
        /// </summary>
        /// <param name="entities">List of Entities to create.</param>
        /// <returns></returns>
        Task AddRange(IEnumerable<T> entities);

        /// <summary>
        /// Deletes the Entity from DB asynchronous
        /// </summary>
        /// <param name="id">Id of the desired Entity to delete.</param>
        /// <returns></returns>
        Task Delete(int id);
        /// <summary>
        /// Deletes many Entities from DB asynchronous
        /// </summary>
        /// <param name="ids">List of ids of desired Entities to delete.</param>
        /// <returns></returns>
        Task DeleteRange(IEnumerable<int> ids);

        /// <summary>
        /// Updates the Entity on DB synchronous
        /// </summary>
        /// <param name="entity">Entity to update.</param>
        void Update(T entity);
        /// <summary>
        /// Updates the Entity on DB asynchronous
        /// </summary>
        /// <param name="id">Id of Entity to update.</param>
        /// <param name="entity">Entity to update.</param>
        /// <returns></returns>
        Task UpdateAsync(int id, T entity);
        /// <summary>
        /// Updates many Entities on DB asynchronous
        /// </summary>
        /// <param name="ids">List of Ids of Entities to update.</param>
        /// <param name="entities">Entities to update.</param>
        /// <returns></returns>
        Task UpdateRangeAsync(IEnumerable<int> ids, IEnumerable<T> entities);
    }

    /// <summary></summary><typeparam name="T">DataModel</typeparam>
    public class BaseRepository<T> : IRepository<T> where T : Base
    {
        /// <summary></summary>
        protected readonly ApplicationContext _context;
        /// <summary></summary>
        protected readonly DbSet<T> _entities;
        /// <summary></summary><param name="context"></param>
        public BaseRepository(ApplicationContext context)
        {
            _context = context;
            _entities = context.Set<T>();
        }

        /// <summary>
        /// Gets all Entities in DB.
        /// </summary>
        /// <returns></returns>
        public virtual async Task<List<T>> GetAll()
        {
            var result = await _entities.ToListAsync();
            return result;
        }
        /// <summary>
        /// Gets an Entity filtered by Id.
        /// </summary>
        /// <param name="id">Id of the desired entity.</param>
        /// <returns></returns>
        public virtual async Task<T?> GetById(int id)
        {
            // Tracked, not AsNoTracking: EF's relationship fix-up is what fills a record's children
            // when the whole table is loaded, and a single row gets none of it. Loading the
            // collection explicitly keeps GetById consistent with GetAll.
            var result = await _entities.FirstOrDefaultAsync(x => x.Id == id);
            if (result is null) return null;

            var entry = _context.Entry(result);
            foreach (var collection in entry.Collections)
            {
                await collection.LoadAsync();
            }

            return result;
        }
        /// <summary>
        /// Gets all entities that satisfy the condition.
        /// </summary>
        /// <param name="condition">Expression of the condition to satisfy.</param>
        /// <returns></returns>
        public virtual async Task<List<T>> GetAllByCondition(Expression<Func<T, bool>> condition)
        {
            return await _entities.Where(condition).ToListAsync();
        }

        /// <summary>
        /// Adds a new Entity to DB asynchronous
        /// </summary>
        /// <param name="entity">Entity to create.</param>
        /// <returns></returns>
        public virtual async Task Add(T entity)
        {
            entity.UpdatedAt = DateTime.UtcNow;
            await _entities.AddAsync(entity);
            await _context.SaveChangesAsync();
        }
        /// <summary>
        /// Adds many Entities to DB asynchronous
        /// </summary>
        /// <param name="entities">List of Entities to create.</param>
        /// <returns></returns>
        public virtual async Task AddRange(IEnumerable<T> entities)
        {
            foreach (var item in entities)
            {
                item.UpdatedAt = DateTime.UtcNow;
            }
            await _entities.AddRangeAsync(entities);
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Deletes the Entity from DB asynchronous
        /// </summary>
        /// <param name="id">Id of the desired Entity to delete.</param>
        /// <returns></returns>
        public virtual async Task Delete(int id)
        {
            T? entity = await GetById(id);
            if (entity == null) return;
            if( entity.IsActive)
            {
                entity.UpdatedAt = DateTime.UtcNow;
                entity.IsActive = false;
                Update(entity);
            }
            else
            {
                _entities.Remove(entity);
                await _context.SaveChangesAsync();
            }
        }
        /// <summary>
        /// Deletes many Entities from DB asynchronous
        /// </summary>
        /// <param name="ids">List of ids of desired Entities to delete.</param>
        /// <returns></returns>
        public virtual async Task DeleteRange(IEnumerable<int> ids)
        {
            var deleteEntities = new List<T>();
            var softDeleteEntities = new List<T>();
            foreach (var item in ids)
            {
                T? result = await GetById(item);
                if (result == null) continue;
                if (result.IsActive)
                {
                    result.UpdatedAt = DateTime.UtcNow;
                    result.IsActive = false;
                    softDeleteEntities.Add(result);
                }
                else
                {
                    deleteEntities.Add(result);
                }
            }
            _entities.UpdateRange(softDeleteEntities);
            _entities.RemoveRange(deleteEntities);
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Updates the Entity on DB synchronous
        /// </summary>
        /// <param name="entity">Entity to update.</param>
        public virtual void Update(T entity)
        {
            entity.UpdatedAt = DateTime.UtcNow;
            _entities.Update(entity);
            _context.SaveChanges();
        }
        /// <summary>
        /// Updates the Entity on DB asynchronous
        /// </summary>
        /// <param name="id">Id of Entity to update.</param>
        /// <param name="entity">Entity to update.</param>
        /// <returns></returns>
        public virtual async Task UpdateAsync(int id, T entity)
        {
            var result = await _entities.AsNoTracking().FirstOrDefaultAsync(x=>x.Id == id);
            if (result == null)
                return;
            entity.UpdatedAt = DateTime.UtcNow;
            _entities.Update(entity);
            await _context.SaveChangesAsync();
        }
        /// <summary>
        /// Updates many Entities on DB asynchronous
        /// </summary>
        /// <param name="ids">List of Ids of Entities to update.</param>
        /// <param name="entities">Entities to update.</param>
        /// <returns></returns>
        public virtual async Task UpdateRangeAsync(IEnumerable<int> ids, IEnumerable<T> entities)
        {
            var entityList = entities.ToList();
            foreach (var id in ids)
            {
                var result = await _entities.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
                if (result == null)
                {
                    var entity = entities.FirstOrDefault(x => x.Id.Equals(id));
                    if(entity != null)
                        entityList.Remove(entity);
                }
            }
            foreach (var entity in entityList)
            {
                entity.UpdatedAt= DateTime.UtcNow;
            }
            _entities.UpdateRange(entityList);
            await _context.SaveChangesAsync();
        }
    }
}
