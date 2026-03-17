namespace Project.Core
{
    /// <summary>
    /// Очень простая "передача параметров" между сценами.
    /// На старте проекта этого достаточно.
    /// Позже заменим на более чистую систему.
    /// </summary>
    public static class LaunchParams
    {
        public static bool IsHost = true;
        public static string Address = "localhost";
    }
}
