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
        /// Deletes the Entity from DB asynchronous.
        /// </summary>
        /// <param name="id">Id of the desired Entity to delete.</param>
        /// <returns>False when no row the caller may delete carries that id.</returns>
        Task<bool> Delete(int id);
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
        Task<bool> UpdateAsync(int id, T entity);
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
            entity.Id = 0;
            AssignTenant(entity);
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
            var entityList = entities.ToList();
            foreach (var item in entityList)
            {
                item.Id = 0;
                AssignTenant(item);
                item.UpdatedAt = DateTime.UtcNow;
            }
            await _entities.AddRangeAsync(entityList);
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Deletes the Entity from DB asynchronous.
        /// </summary>
        /// <param name="id">Id of the desired Entity to delete.</param>
        /// <returns>False when no row the caller may delete carries that id.</returns>
        public virtual async Task<bool> Delete(int id)
        {
            // GetById is filtered by HasQueryFilter(IsActive), so an inactive row came back null and
            // the physical-delete branch below could never be reached: a soft-deleted row was stuck
            // forever. The lookup has to see the row the caller is asking about.
            T? entity = await FindDeletableRow(id);
            if (entity == null) return false;
            if( entity.IsActive)
            {
                entity.UpdatedAt = DateTime.UtcNow;
                entity.IsActive = false;
                Update(entity);
            }
            else
            {
                _entities.Remove(entity);
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // Rows still pointing at this one (a constant with live children) block the hard
                    // delete; the database is right and the caller asked for a delete that already
                    // happened. Detach and leave it soft-deleted instead of turning a second DELETE
                    // into a 500 the UI cannot explain.
                    _context.Entry(entity).State = EntityState.Unchanged;
                }
            }
            return true;
        }
        /// <summary>
        /// Deletes many Entities from DB asynchronous
        /// </summary>
        /// <param name="ids">List of ids of desired Entities to delete.</param>
        /// <returns></returns>
        public virtual async Task DeleteRange(IEnumerable<int> ids)
        {
            var deleteEntities = new List<T>();
            foreach (var item in ids)
            {
                T? result = await FindDeletableRow(item);
                if (result == null) continue;
                if (result.IsActive)
                {
                    result.UpdatedAt = DateTime.UtcNow;
                    result.IsActive = false;
                }
                else
                {
                    deleteEntities.Add(result);
                }
            }
            _entities.RemoveRange(deleteEntities);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Same rule as Delete: a row the database refuses to drop (children still point at
                // it) stays soft-deleted instead of failing the whole batch with a 500.
                foreach (var entity in deleteEntities) _context.Entry(entity).State = EntityState.Unchanged;
            }
        }

        /// <summary>
        /// Updates the Entity on DB synchronous
        /// </summary>
        /// <param name="entity">Entity to update.</param>
        public virtual void Update(T entity)
        {
            var persisted = _entities.FirstOrDefault(current => current.Id == entity.Id);
            if (persisted is null) return;
            CopyClientValues(entity, persisted);
            _context.SaveChanges();
        }
        /// <summary>
        /// Updates the Entity on DB asynchronous
        /// </summary>
        /// <param name="id">Id of Entity to update.</param>
        /// <param name="entity">Entity to update.</param>
        /// <returns>False when no row visible to the caller carries that id.</returns>
        public virtual async Task<bool> UpdateAsync(int id, T entity)
        {
            var persisted = await _entities.FirstOrDefaultAsync(current => current.Id == id);
            if (persisted is null) return false;
            CopyClientValues(entity, persisted);
            await _context.SaveChangesAsync();
            return true;
        }
        /// <summary>
        /// Updates many Entities on DB asynchronous
        /// </summary>
        /// <param name="ids">List of Ids of Entities to update.</param>
        /// <param name="entities">Entities to update.</param>
        /// <returns></returns>
        public virtual async Task UpdateRangeAsync(IEnumerable<int> ids, IEnumerable<T> entities)
        {
            var byId = entities.GroupBy(entity => entity.Id).ToDictionary(group => group.Key, group => group.First());
            foreach (var id in ids.Distinct())
            {
                if (!byId.TryGetValue(id, out var source)) continue;
                var persisted = await _entities.FirstOrDefaultAsync(current => current.Id == id);
                if (persisted is not null) CopyClientValues(source, persisted);
            }
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// The row a delete may reach: the caller's own organization, plus tenant 0 for the one role
        /// that owns the shared vocabulary. A shared row (a seed <c>Constants</c> row) carries no
        /// organization, so scoping the lookup to <see cref="ApplicationContext.CurrentTenantId"/>
        /// alone made it invisible and the delete answered 204 having found nothing.
        /// </summary>
        private async Task<T?> FindDeletableRow(int id)
        {
            var query = _entities.IgnoreQueryFilters().Where(row => row.Id == id);
            query = _context.IsSuperadmin
                ? query.Where(row => row.TenantId == _context.CurrentTenantId || row.TenantId == 0)
                : query.Where(row => row.TenantId == _context.CurrentTenantId);
            return await query.FirstOrDefaultAsync();
        }

        private static readonly HashSet<string> ServerControlledProperties = new(StringComparer.Ordinal)
        {
            nameof(Base.Id), nameof(Base.IsActive), nameof(Base.CreatedAt), nameof(Base.CreatedBy),
            nameof(Base.UpdatedAt), nameof(Base.UpdatedBy), nameof(Base.TenantId)
        };

        private void CopyClientValues(T source, T persisted)
        {
            var sourceValues = _context.Entry(source).CurrentValues;
            var persistedValues = _context.Entry(persisted).CurrentValues;
            foreach (var property in persistedValues.Properties)
            {
                if (!ServerControlledProperties.Contains(property.Name))
                    persistedValues[property.Name] = sourceValues[property.Name];
            }
        }
        private void AssignTenant(T entity)
        {
            entity.TenantId = entity is Constants constant && Services.TenantScope.IsShared(constant)
                ? 0 : _context.CurrentTenantId;
            if (entity is Item item)
                foreach (var attachment in item.Attachments) attachment.TenantId = entity.TenantId;
        }
    }
}
