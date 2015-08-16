using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.Objects
{
    public static class TikConnectionExtensions
    {
        public static IEnumerable<TEntity> LoadList<TEntity>(this ITikConnection connection)
            where TEntity : new()
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();

            ITikCommand command = connection.CreateCommand(metadata.EntityPath + "/print");
            if (metadata.IncludeDetails)
                command.Parameters.Add(connection.CreateParameter("detail", ""));

            return LoadList<TEntity>(command);
        }

        public static IEnumerable<TEntity> LoadList<TEntity>(ITikCommand command)
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
                object propValue = GetValueFromSentence(sentence, property);
                property.PropertyIfo.SetValue(result, propValue); //NOTE: works event if setter is private
            }

            return result;
        }

        private static object GetValueFromSentence(ITikReSentence sentence, TikEntityPropertyDescriptor property)
        {
            //Read field value (or get default value)
            string wordValue;
            if (property.IsMandatory)
                wordValue = sentence.GetResponseField(property.FieldName);
            else
                wordValue = sentence.GetResponseFieldOrDefault(property.FieldName, property.DefaultValue);


            //convert to property type            
            if (property.PropertyType == typeof(string))
                return wordValue;
            else if (property.PropertyType == typeof(int))
                return int.Parse(wordValue);
            else if (property.PropertyType == typeof(long))
                return long.Parse(wordValue);
            else
                throw new NotImplementedException(string.Format("Property type {0} not supported.", property.PropertyType));
        }
    }
}
