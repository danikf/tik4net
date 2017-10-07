using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace tik4net.Objects
{
    /// <summary>
    /// Main mapper extension - extends <see cref="ITikConnection"/>.
    /// Supports CRUD and move functions.
    /// <para>
    /// <list type="bullet">
    /// <listheader>Load:</listheader>
    /// <item><see cref="LoadAll"/></item>
    /// <item><see cref="LoadById"/></item>
    /// <item><see cref="LoadList{TEntity}(ITikConnection, ITikCommandParameter[])"/></item>
    /// <item><see cref="LoadWithDuration"/></item>
    /// <item><see cref="LoadAsync"/></item>
    /// </list>
    /// </para>
    /// 
    /// <para>
    /// <list type="bullet">
    /// <listheader>Save:</listheader>
    /// <item><see cref="Save"/> (Insert/Update)</item>
    /// <item><see cref="SaveListDifferences"/> (Insert/Update/Delete)</item>
    /// </list>
    /// </para>
    /// 
    /// <para>
    /// <list type="bullet">
    /// <listheader>Delete:</listheader>
    /// <item><see cref="Delete"/></item>
    /// </list>
    /// </para>
    /// 
    /// <para>
    /// <list type="bullet">
    /// <listheader>Move:</listheader>
    /// <item><see cref="Move"/></item>
    /// <item><see cref="MoveToEnd"/></item>
    /// </list>
    /// </para>
    /// </summary>
    public static class TikConnectionExtensions
    {
        #region -- LOAD --
        /// <summary>
        /// Alias to <see cref="LoadList{TEntity}(ITikConnection, ITikCommandParameter[])"/> without filter.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <returns>Loaded list of entities.</returns>
        public static IEnumerable<TEntity> LoadAll<TEntity>(this ITikConnection connection)
            where TEntity : new()
        {
            return LoadList<TEntity>(connection);
        }

        /// <summary>
        /// Alias to <see cref="LoadList{TEntity}(ITikConnection, ITikCommandParameter[])"/> optionaly with filter, ensures that result contains exactly one row.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="filterParameters">Optional list of filter parameters (interpreted as connected with AND)</param>
        /// <returns>Loaded single entity.</returns>
        public static TEntity LoadSingle<TEntity>(this ITikConnection connection, params ITikCommandParameter[] filterParameters)
            where TEntity : new()
        {
            return LoadList<TEntity>(connection, filterParameters).Single();
        }

        /// <summary>
        /// Alias to <see cref="LoadList{TEntity}(ITikConnection, ITikCommandParameter[])"/> without filter, ensures that result contains exactly one row.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="filterParameters">Optional list of filter parameters (interpreted as connected with AND)</param>
        /// <returns>Loaded single entity or null.</returns>
        public static TEntity LoadSingleOrDefault<TEntity>(this ITikConnection connection, params ITikCommandParameter[] filterParameters)
            where TEntity : new()
        {
            return LoadList<TEntity>(connection, filterParameters).SingleOrDefault();
        }

        /// <summary>
        /// Loads entity with specified id. Returns null if not found.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="id">Entity id.</param>
        /// <returns>Loaded entity or null.</returns>
        public static TEntity LoadById<TEntity>(this ITikConnection connection, string id)
            where TEntity : new()
        {
            return LoadList<TEntity>(connection, connection.CreateParameter(TikSpecialProperties.Id, id)).SingleOrDefault();
        }

        /// <summary>
        /// Loads entity list. Could be filtered with <paramref name="filterParameters"/>.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="filterParameters">Optional list of filter parameters (interpreted as connected with AND)</param>
        /// <returns>List (or empty list) of loaded entities.</returns>
        /// <seealso cref="TikCommandExtensions.LoadList{TEntity}(ITikCommand)"/>
        public static IEnumerable<TEntity> LoadList<TEntity>(this ITikConnection connection, params ITikCommandParameter[] filterParameters)
            where TEntity : new()
        {
            var command = CreateLoadCommandWithFilter<TEntity>(connection, "/print", TikCommandParameterFormat.Filter, filterParameters);
            return command.LoadList<TEntity>();
        }

        /// <summary>
        /// Calls command and reads all returned rows for given <paramref name="durationSec"/> period.
        /// After this period calls cancell to mikrotik router and returns all loaded rows.
        /// Throws exception if any 'trap' row occurs.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="durationSec">Loading period.</param>
        /// <param name="parameters">Optional list of filters/parameters (interpreted as connected with AND)</param>
        /// <returns>List (or empty list) of loaded entities.</returns>
        /// <seealso cref="TikCommandExtensions.LoadWithDuration{TEntity}(ITikCommand, int)"/>
        public static IEnumerable<TEntity> LoadWithDuration<TEntity>(this ITikConnection connection, int durationSec, params ITikCommandParameter[] parameters)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, "connection");

            var command = CreateLoadCommandWithFilter<TEntity>(connection, "", TikCommandParameterFormat.NameValue, parameters);

            return command.LoadWithDuration<TEntity>(durationSec);
        }


        /// <summary>
        /// Calls command and starts backgroud reading thread. After that returns control to calling thread.
        /// All read rows are returned as callbacks (<paramref name="onLoadItemCallback"/>, <paramref name="onExceptionCallback"/>) from loading thread.
        /// REMARKS: if you want to propagate loaded values to GUI, you should use some kind of synchronization or Invoke, because 
        /// callbacks are called from non-ui thread.
        /// The running load can be terminated by <see cref="ITikCommand.Cancel"/> or <see cref="ITikCommand.CancelAndJoin()"/> call. 
        /// Command is returned as result of the method.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="onLoadItemCallback">Callback called for each loaded !re row</param>
        /// <param name="onExceptionCallback">Callback called when error occurs (!trap row is returned)</param>
        /// <param name="parameters">Optional list of filters/parameters (interpreted as connected with AND)</param>
        /// <returns><see cref="ITikCommand"/> which is already running the async load operation. You can cancel the running operation by <see cref="ITikCommand.Cancel"/> method call.</returns>
        /// <seealso cref="TikCommandExtensions.LoadAsync{TEntity}(ITikCommand, Action{TEntity}, Action{Exception})"/>
        public static ITikCommand LoadAsync<TEntity>(this ITikConnection connection,
            Action<TEntity> onLoadItemCallback, Action<Exception> onExceptionCallback = null,
            params ITikCommandParameter[] parameters)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, "connection");
            Guard.ArgumentNotNull(onLoadItemCallback, "onLoadItemCallback");

            var command = CreateLoadCommandWithFilter<TEntity>(connection, "", TikCommandParameterFormat.NameValue, parameters);

            command.LoadAsync<TEntity>(onLoadItemCallback, onExceptionCallback);
            return command;
        }

        private static ITikCommand CreateLoadCommandWithFilter<TEntity> (ITikConnection connection, string commandSufix, TikCommandParameterFormat defaultParameterFormat, ITikCommandParameter[] parameters)
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();

            ITikCommand command = connection.CreateCommand(metadata.EntityPath + commandSufix, defaultParameterFormat);

            // =detail=
            if (metadata.IncludeDetails)
                command.AddParameter("detail", "", TikCommandParameterFormat.NameValue);
            //.proplist
            if (metadata.IncludeProplist)
                command.AddParameter(TikSpecialProperties.Proplist, string.Join(",", metadata.Properties.Select(prop => prop.FieldName).ToArray()), TikCommandParameterFormat.NameValue);
            //filter
            //parameters
            if (parameters != null)
            {
                foreach (ITikCommandParameter param in parameters)
                {
                    command.Parameters.Add(param);
                }
            }

            return command;
        }

        #endregion

        #region -- SAVE --
        private static void EnsureNotReadonlyEntity(TikEntityMetadata entityMetadata)
        {
            if (entityMetadata.IsReadOnly)
                throw new InvalidOperationException("Can not save R/O entity.");
        }

        private static void EnsureSupportsOrdering(TikEntityMetadata entityMetadata)
        {
            if (!entityMetadata.IsOrdered)
                throw new InvalidOperationException("Can not move entity without ordering support.");
        }

        private static void EnsureHasIdProperty(TikEntityMetadata metadata)
        {
            if (!metadata.HasIdProperty)
                throw new InvalidOperationException(string.Format("Can not update/delete non-sigleton entity which doesn't contains property for '{0}' field.", TikSpecialProperties.Id));
        }

        /// <summary>
        /// Saves entity to mikrotik router. Does insert (/add) whan entity has empty id and update(/set + /unset) when id is present).
        /// Behavior of save is modified via <see cref="TikPropertyAttribute"/> on properties.
        /// See <see cref="TikPropertyAttribute.DefaultValue"/>, <see cref="TikPropertyAttribute.UnsetOnDefault"/>.
        /// </summary>
        /// <typeparam name="TEntity">Saved entitie type.</typeparam>
        /// <param name="connection">Tik connection used to save.</param>
        /// <param name="entity">Saved entity.</param>
        /// <param name="usedFieldsFilter">List of field names (on mikrotik) which should be modified. If is not null, only listed fields will be modified.</param>
        public static void Save<TEntity>(this ITikConnection connection, TEntity entity, IEnumerable<string> usedFieldsFilter = null)
            where TEntity:new()
        {            
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            EnsureNotReadonlyEntity(metadata);

            string id;
            if (metadata.IsSingleton)
                id = null;
            else
            {
                EnsureHasIdProperty(metadata);
                id = metadata.IdProperty.GetEntityValue(entity);
            }

            if (!metadata.IsSingleton && string.IsNullOrEmpty(id))
            {
                //create
                ITikCommand createCmd = connection.CreateCommand(metadata.EntityPath + "/add", TikCommandParameterFormat.NameValue);

                foreach (var property in metadata.Properties
                    .Where(pm => !pm.IsReadOnly)
                    .Where(pm => usedFieldsFilter == null || usedFieldsFilter.Contains(pm.FieldName, StringComparer.OrdinalIgnoreCase)))
                {
                    if (!property.HasDefaultValue(entity))
                    {
                        createCmd.AddParameter(property.FieldName, property.GetEntityValue(entity));
                    }
                }

                id = createCmd.ExecuteScalar();
                if (metadata.HasIdProperty)
                    metadata.IdProperty.SetEntityValue(entity, id); // update saved id into entity
            }
            else
            {
                //update (set+unset)
                ITikCommand setCmd = connection.CreateCommand(metadata.EntityPath + "/set", TikCommandParameterFormat.NameValue);

                if (!metadata.IsSingleton && usedFieldsFilter == null)
                {
                    //compare state on mikrotik and update different fields only
                    var unmodifiedEntity =  connection.LoadById<TEntity>(id); //TODO some kind of "loaded entities" session cache could be used to avoid another load before save.
                    usedFieldsFilter = entity.GetDifferentFields(unmodifiedEntity);
                }

                List<string> fieldsToUnset = new List<string>();

                foreach (var property in metadata.Properties
                    .Where(pm => !pm.IsReadOnly)
                    .Where(pm => usedFieldsFilter == null || usedFieldsFilter.Contains(pm.FieldName, StringComparer.OrdinalIgnoreCase)))
                {
                    if (property.HasDefaultValue(entity) && property.UnsetOnDefault)
                        fieldsToUnset.Add(property.FieldName);
                    else
                        setCmd.AddParameter(property.FieldName, property.GetEntityValue(entity)); //full update (all values)                        
                }

                if (fieldsToUnset.Count > 0)
                {
                    // this should also work (see http://forum.mikrotik.com/viewtopic.php?t=28821 )
                    //ip/route/unset
                    //=.id = *1
                    //= value-name=routing-mark

                    foreach (string fld in fieldsToUnset)
                    {
                        ITikCommand unsetCmd = connection.CreateCommand(metadata.EntityPath + "/unset", TikCommandParameterFormat.NameValue);
                        unsetCmd.AddParameter(TikSpecialProperties.Id, id, TikCommandParameterFormat.NameValue);
                        unsetCmd.AddParameter(TikSpecialProperties.UnsetValueName, fld);

                        unsetCmd.ExecuteNonQuery();
                    }
                }
                if (setCmd.Parameters.Any())
                {
                    if (!metadata.IsSingleton)
                        setCmd.AddParameter(TikSpecialProperties.Id, id, TikCommandParameterFormat.NameValue);
                    setCmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// List version of <see cref="Save"/> method. Saves differences between given <paramref name="modifiedList"/> and <paramref name="unmodifiedList"/>.
        /// Typical usage is: Load, create list clone, modify list, save diferences.
        /// </summary>
        /// <example>
        /// var list = connection.LoadList{FirewallAddressList}(connection.CreateParameter("list", listName), connection.CreateParameter("address", ipAddress));
        /// var listClonedBackup = list.CloneEntityList(); //creates clone of all entities in list
        /// list.Add(new FirewallAddressList() {Address = ipAddress, List = listName, }); //insert
        /// list[0].Comment = "test comment"; //update
        /// list.RemoveAt(1); //delete
        /// connection.SaveListDifferences(list, listClonedBackup);
        /// </example>
        /// <typeparam name="TEntity">Saved entitie type.</typeparam>
        /// <param name="connection">Tik connection used to save.</param>
        /// <param name="modifiedList">List with modifications.</param>
        /// <param name="unmodifiedList">Original (cloned) unmodified list.</param>
        /// <seealso cref="TikEntityObjectsExtensions.CloneEntity"/>
        /// <seealso cref="TikEntityObjectsExtensions.CloneEntityList"/>
        /// <seealso cref="Save"/>
        public static void SaveListDifferences<TEntity>(this ITikConnection connection, IEnumerable<TEntity> modifiedList, IEnumerable<TEntity> unmodifiedList)
            where TEntity : new()
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            EnsureNotReadonlyEntity(metadata);
            EnsureHasIdProperty(metadata);
            var idProperty = metadata.IdProperty;

            var entitiesToCreate = modifiedList.Where(entity => string.IsNullOrEmpty(idProperty.GetEntityValue(entity))).ToList(); // new items in modifiedList

            Dictionary<string, TEntity> modifiedEntities = modifiedList
                .Where(entity => !string.IsNullOrEmpty(idProperty.GetEntityValue(entity)))
                .ToDictionary(entity => idProperty.GetEntityValue(entity)); //all entities from modified list with ids
            Dictionary<string, TEntity> unmodifiedEntities = unmodifiedList
                //.Where(entity => !string.IsNullOrEmpty(idProperty.GetEntityValue(entity))) - entity in unmodified list has id (is loaded from miktrotik)
                .ToDictionary(entity => idProperty.GetEntityValue(entity)); //all entities from unmodified list with ids

            //DELETE
            foreach (string entityId in unmodifiedEntities.Keys.Where(id => !modifiedEntities.ContainsKey(id))) //missing in modified -> deleted
            {
                Delete(connection, unmodifiedEntities[entityId]);
            }

            //CREATE
            foreach (TEntity entity in entitiesToCreate)
            {
                Save(connection, entity);
            }

            //UPDATE
            foreach (string entityId in unmodifiedEntities.Keys.Where(id => modifiedEntities.ContainsKey(id))) // are in both modified and unmodified -> compare values (update/skip)
            {
                TEntity modifiedEntity = modifiedEntities[entityId];
                TEntity unmodifiedEntity = unmodifiedEntities[entityId];

                if (!modifiedEntity.EntityEquals(unmodifiedEntity))
                {
                    Save(connection, modifiedEntity);
                }
            }

            //TODO support for order!
        }
        #endregion

        #region -- DELETE --
        /// <summary>
        /// Deletes entity (.id is the key) on mikrotik router.
        /// </summary>
        /// <typeparam name="TEntity">Deleted entity type.</typeparam>
        /// <param name="connection">Tik connection used to delete entity.</param>
        /// <param name="entity">Entity to be deleted (.id property is the key)</param>
        public static void Delete<TEntity>(this ITikConnection connection, TEntity entity)
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            EnsureNotReadonlyEntity(metadata);
            EnsureHasIdProperty(metadata);
            string id = metadata.IdProperty.GetEntityValue(entity);
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Entity has no .id (entity is not loaded from mikrotik router)", "entity");

            ITikCommand cmd = connection.CreateCommandAndParameters(metadata.EntityPath + "/remove", TikCommandParameterFormat.NameValue,
                TikSpecialProperties.Id, id);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Deletes all entities of given type on mikrotik router.
        /// </summary>
        /// <typeparam name="TEntity">Deleted entity type.</typeparam>
        /// <param name="connection">Tik connection used to delete entity.</param>
        /// <returns>Number of deleted entities. </returns>
        public static int DeleteAll<TEntity>(this ITikConnection connection)
            where TEntity : new()
        {
            var list = connection.LoadAll<TEntity>();
            int result = list.Count();

            connection.SaveListDifferences(new List<TEntity>() /*empty list as expected => delete all*/, list);

            return result;
        }
        #endregion

        #region -- MOVE --
        /// <summary>
        /// Moves given <paramref name="entityToMove"/> before given <paramref name="entityToMoveBefore"/>.
        /// </summary>
        /// <typeparam name="TEntity">Moved entity type.</typeparam>
        /// <param name="connection">Tik connection used to move entity.</param>
        /// <param name="entityToMove">Entity to be moved.</param>
        /// <param name="entityToMoveBefore">Entity before which is given <paramref name="entityToMove"/> moved.</param>
        public static void Move<TEntity>(this ITikConnection connection, TEntity entityToMove, TEntity entityToMoveBefore)
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            EnsureSupportsOrdering(metadata);
            EnsureHasIdProperty(metadata);

            string idToMove = metadata.IdProperty.GetEntityValue(entityToMove);
            string idToMoveBefore = entityToMoveBefore != null ? metadata.IdProperty.GetEntityValue(entityToMoveBefore) : null;

            ITikCommand cmd = connection.CreateCommandAndParameters(metadata.EntityPath + "/move", TikCommandParameterFormat.NameValue,
                "numbers", idToMove);

            if (entityToMoveBefore != null)
                cmd.AddParameter("destination", idToMoveBefore);

            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Moves given <paramref name="entityToMove"/> to the end (make it last entity in the list).
        /// </summary>
        /// <typeparam name="TEntity">Moved entity type.</typeparam>
        /// <param name="connection">Tik connection used to move entity.</param>
        /// <param name="entityToMove">Entity to be moved.</param>
        public static void MoveToEnd<TEntity>(this ITikConnection connection, TEntity entityToMove)
        {
            Move(connection, entityToMove, default(TEntity));
        }

        #endregion

        #region -- MERGE --
        /// <summary>
        /// Creates merge object. This object should be setuped (via fluent API) and finaly <see cref="TikListMerge{TEntity}.Save"/> must be called.
        /// </summary>
        /// <typeparam name="TEntity">Type of the entity in list to merge.</typeparam>
        /// <param name="connection">Tik connection used to update state of entities.</param>
        /// <param name="expected">Expected state on mikrotik router (Missing items will be added, others will be updated if are different).</param>
        /// <param name="original">Actual state on mikrotik router. (Surplus items will be deleted).</param>
        /// <returns>Merge object, that should be setuped (via fluent API) and finaly <see cref="TikListMerge{TEntity}.Save"/> must be called on this object to perform operations on mikrotik router.</returns>
        /// <example>
        /// var original = connection.LoadAll{QueueTree}().Where(q => q.Name == "Q1" || q.Name == "Q2" || q.Name.StartsWith("Q3")); //just subset of actual QT items
        /// string unique = Guid.NewGuid().ToString();
        /// List{QueueTree} expected = new List{QueueTree}()  //new expected subset of QT items
        ///    {
        ///        new QueueTree() { Name = "Q1", Parent = "global", PacketMark = "PM1" },
        ///        new QueueTree() { Name = "Q2", Parent = "global", PacketMark = "PM2", Comment = unique }, //always update
        ///        new QueueTree() { Name = "Q3 " + unique, Parent = "global", PacketMark = "PM3" }, // always insert + delete from previous run
        ///    };
        /// connection.CreateMerge(expected, original) //access to merge object            
        ///    .WithKey(queue => queue.Name) // items with the same name are the same (name is the key)
        ///    .Field(q => q.Parent)         // we are updating just Parent, PacketMark and Comment fields
        ///    .Field(q => q.PacketMark)
        ///    .Field(q => q.Comment)
        ///    .Save();                      // modify mikrotik router QueueTree 
        /// </example>
        public static TikListMerge<TEntity> CreateMerge<TEntity>(this ITikConnection connection, IEnumerable<TEntity> expected, IEnumerable<TEntity> original)
            where TEntity: new()
        {
            return new TikListMerge<TEntity>(connection, expected, original);
        }

        #endregion
    }
}
