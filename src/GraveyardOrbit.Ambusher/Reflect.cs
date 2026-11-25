using HarmonyLib;

namespace GraveyardOrbit.Ambusher
{
    /// <summary>Small reflection helpers for the private members we need in the prototype.</summary>
    internal static class Reflect
    {
        internal static object Get(object target, string field)
        {
            if (target == null) return null;
            return AccessTools.Field(target.GetType(), field)?.GetValue(target);
        }

        internal static T Get<T>(object target, string field)
        {
            object value = Get(target, field);
            return value is T typed ? typed : default;
        }

        internal static void Set(object target, string field, object value)
        {
            if (target == null) return;
            AccessTools.Field(target.GetType(), field)?.SetValue(target, value);
        }

        internal static object Call(object target, string method, params object[] args)
        {
            if (target == null) return null;
            return AccessTools.Method(target.GetType(), method)?.Invoke(target, args);
        }
    }
}
