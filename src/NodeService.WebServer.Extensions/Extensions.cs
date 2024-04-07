namespace NodeService.WebServer.Extensions
{
    public static class Extensions
    {
        public static bool IsNullOrEmpty<T>(this IEnumerable<T>? src)
        {
            return src == null || !src.Any();
        }

        public static bool IsNullOrAny<T>(this IEnumerable<T>? src, Func<T, bool> func)
        {
            return src == null || !src.Any() || src.Any(func);
        }

    }
}
