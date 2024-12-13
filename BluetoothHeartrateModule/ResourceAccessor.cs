using System.Reflection;

namespace BluetoothHeartrateModule
{
    internal static class ResourceAccessor
    {
        public static Uri Get(string resourcePath)
        {
            var assemblyName = Assembly.GetExecutingAssembly().GetName();
            var uri = string.Format(
                "/{0};V{1};component/{2}",
                assemblyName.Name,
                assemblyName.Version,
                resourcePath
            );

            return new Uri(uri, UriKind.Relative);
        }
    }
}
