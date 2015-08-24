using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.Objects
{
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

        public static TEntity LoadById<TEntity>(this ITikConnection connection, string id)
            where TEntity : new()
        {
            return LoadList<TEntity>(connection, connection.CreateParameter(".id", id)).SingleOrDefault();
        }

        public static IEnumerable<TEntity> LoadList<TEntity>(this ITikConnection connection, params ITikCommandParameter[] filter)
            where TEntity : new()
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();

            ITikCommand command = connection.CreateCommand(metadata.EntityPath + "/print");
            if (metadata.IncludeDetails)
                command.IncludeDetails = true;
            foreach(ITikCommandParameter filterParam in filter)
            {
                command.Parameters.Add(filterParam);
            }

            return LoadList<TEntity>(command);
        }

        private static IEnumerable<TEntity> LoadList<TEntity>(ITikCommand command)
            where TEntity : new()
        {            
            var responseSentences = command.ExecuteList();

            return responseSentences.Select(sentence => CreateObject<TEntity>(sentence)).ToList();
        }

        private static TEntity CreateObject<TEntity>(ITikReSentence sentence)
            where TEntity: new()
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();

            TEntity result = new TEntity();
            foreach(var property in metadata.Properties)
            {
                property.SetEntityValue(result, GetValueFromSentence(sentence, property)); 
            }

            return result;
        }

        private static string GetValueFromSentence(ITikReSentence sentence, TikEntityPropertyAccessor property)
        {
            //Read field value (or get default value)
            if (property.IsMandatory)
                return sentence.GetResponseField(property.FieldName);
            else
                return sentence.GetResponseFieldOrDefault(property.FieldName, property.DefaultValue);
        }

        #endregion

        #region -- SAVE --
        private static void EnsureNotReadonlyEntity(TikEntityMetadata entityMetadata)
        {
            if (entityMetadata.IsReadOnly)
                throw new InvalidOperationException("Can not save R/O entity.");
        }

        public static void Save<TEntity>(this ITikConnection connection, TEntity entity)
        {            
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            EnsureNotReadonlyEntity(metadata);
            string id = metadata.IdProperty.GetEntityValue(entity);

            if (string.IsNullOrEmpty(id))
            {
                //create
                ITikCommand createCmd = connection.CreateCommand(metadata.EntityPath + "/add");

                foreach (var property in metadata.Properties.Where(pm => !pm.IsReadOnly))
                {
                    if (!property.HasDefaultValue(entity))
                    {
                        createCmd.AddParameter(property.FieldName, property.GetEntityValue(entity));
                    }
                }

                id = createCmd.ExecuteScalar();
                metadata.IdProperty.SetEntityValue(entity, id); // update saved id into entity
            }
            else
            {
                //update (set+unset)
                ITikCommand setCmd = connection.CreateCommand(metadata.EntityPath + "/set");
                ITikCommand unsetCmd = connection.CreateCommand(metadata.EntityPath + "/unset");

                foreach (var property in metadata.Properties.Where(pm => !pm.IsReadOnly))
                {
                    if (property.HasDefaultValue(entity) && property.UnsetWhenDefault)
                        unsetCmd.AddParameter(property.FieldName, "");
                    else
                        setCmd.AddParameter(property.FieldName, property.GetEntityValue(entity)); //full update (all values)                        
                }

                if (unsetCmd.Parameters.Any())
                {
                    //    //ip/address/unset
                    //    //=.id=...ID...
                    //    //=address=
                    //    //>!done
                    unsetCmd.AddParameter(".id", id);
                    unsetCmd.ExecuteNonQuery();
                    
                    //TODO this should also work (see http://forum.mikrotik.com/viewtopic.php?t=28821 )
                    //ip/route/unset
                    //=.id = *1
                    //= value - name = routing - mark
                }
                if (setCmd.Parameters.Any())
                {
                    setCmd.AddParameter(".id", id);
                    setCmd.ExecuteNonQuery();
                }
            }
        }

        public static void SaveListDifferences<TEntity>(this ITikConnection connection, IEnumerable<TEntity> modifiedList, IEnumerable<TEntity> unmodifiedList)
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            EnsureNotReadonlyEntity(metadata);
            var idProperty = metadata.IdProperty;
            
            var entitiesToCreate = modifiedList.Where(entity => string.IsNullOrEmpty(idProperty.GetEntityValue(entity))).ToList(); // new items in modifiedList

            Dictionary<string, TEntity> modifiedEntities = modifiedList
                .Where(entity => !string.IsNullOrEmpty(idProperty.GetEntityValue(entity)))
                .ToDictionary(entity => idProperty.GetEntityValue(entity)); //all entities from modified list with ids
            Dictionary<string, TEntity> unmodifiedEntities = unmodifiedList
                //.Where(entity => !string.IsNullOrEmpty(idProperty.GetEntityValue(entity))) - entity in unmodified list has id (is loaded from miktrotik)
                .ToDictionary(entity => idProperty.GetEntityValue(entity)); //all entities from unmodified list with ids

            //DELETE
            foreach(string entityId in unmodifiedEntities.Keys.Where(id => !modifiedEntities.ContainsKey(id))) //missing in modified -> deleted
            {
                Delete(connection, unmodifiedEntities[entityId]);
            }

            //CREATE
            foreach (TEntity entity in entitiesToCreate)
            {
                Save(connection, entity);
            }

            //UPDATE
            foreach(string entityId in unmodifiedEntities.Keys.Where(id=> modifiedEntities.ContainsKey(id))) // are in both modified and unmodified -> compare values (update/skip)
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
        public static void Delete<TEntity>(this ITikConnection connection, TEntity entity)
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            EnsureNotReadonlyEntity(metadata);
            string id = metadata.IdProperty.GetEntityValue(entity);
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Entity has no .id (entity is not loaded from mikrotik router)", "entity");

            ITikCommand cmd = connection.CreateCommandAndParameters(metadata.EntityPath + "/remove",
                ".id", id);
            cmd.ExecuteNonQuery();
        }
        #endregion

        #region -- MOVE --
        public static void Move<TEntity>(this ITikConnection connection, TEntity entityToMove, TEntity entityToMoveBefore)
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            string idToMove = metadata.IdProperty.GetEntityValue(entityToMove);
            string idToMoveBefore = entityToMoveBefore != null ? metadata.IdProperty.GetEntityValue(entityToMoveBefore) : null;

            ITikCommand cmd = connection.CreateCommandAndParameters(metadata.EntityPath + "/move",
                "numbers", idToMove);

            if (entityToMoveBefore != null)
                cmd.AddParameter("destination", idToMoveBefore);

            cmd.ExecuteNonQuery();
        }

        public static void MoveToEnd<TEntity>(this ITikConnection connection, TEntity entityToMove)
        {
            Move(connection, entityToMove, default(TEntity));
        }

        #endregion
    }
}
