using System.Reflection;

namespace SoftcodeUnicontaMiddleware.UnicontaService
{
    public static class UnicontaEntitySerializer
    {
        private static readonly HashSet<string> DenyList = new()
        {
            "_CompanyId",
            "_RowId",
            "_Session",
            "_Master",
            "_Changed",
            "_Deleted"
        };

        public static Dictionary<string, object?> Serialize(object entity)
        {
            var result = new Dictionary<string, object?>();

            if (entity == null)
                return result;

            SerializeInto(result, entity, prefix: null);
            return result;
        }

        public static Dictionary<string, object?> SerializeMany(params object[] entities)
        {
            var result = new Dictionary<string, object?>();

            foreach (var entity in entities)
            {
                if (entity == null) continue;

                var prefix = entity.GetType().Name;
                SerializeInto(result, entity, prefix);
            }

            return result;
        }

        private static void SerializeInto(
            Dictionary<string, object?> target,
            object entity,
            string? prefix)
        {
            var type = entity.GetType();

            // ---------------- FIELDS ----------------
            foreach (var field in type.GetFields(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic))
            {
                if (DenyList.Contains(field.Name))
                    continue;

                var key = prefix == null
                    ? field.Name
                    : $"{prefix}.{field.Name}";

                WriteValue(target, key, field.GetValue(entity));
            }

            // ---------------- PROPERTIES ----------------
            foreach (var prop in type.GetProperties(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic))
            {
                // Skip indexers
                if (prop.GetIndexParameters().Length > 0)
                    continue;

                // Skip write-only / unsafe
                if (!prop.CanRead)
                    continue;

                if (DenyList.Contains(prop.Name))
                    continue;

                var key = prefix == null
                    ? prop.Name
                    : $"{prefix}.{prop.Name}";

                object? value;
                try
                {
                    value = prop.GetValue(entity);
                }
                catch
                {
                    continue; // some Uniconta props throw internally
                }

                WriteValue(target, key, value);
            }
        }

        private static void WriteValue(
            Dictionary<string, object?> target,
            string key,
            object? value)
        {
            if (value == null)
            {
                target[key] = null;
                return;
            }

            var type = value.GetType();

            if (type.IsPrimitive ||
                type.IsEnum ||
                type == typeof(string) ||
                type == typeof(decimal) ||
                type == typeof(DateTime) ||
                type == typeof(DateTimeOffset) ||
                type == typeof(Guid) ||
                type == typeof(long))
            {
                target[key] = value;
            }
            else if (Nullable.GetUnderlyingType(type) != null)
            {
                target[key] = value;
            }
            else
            {
                target[key] = value.ToString();
            }
        }
    }
}
