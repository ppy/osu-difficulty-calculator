// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using MySql.Data.MySqlClient;

namespace osu.Server.DifficultyCalculator
{
    public class Database
    {
        public static MySqlConnection GetConnection()
        {
            if (AppSettings.ConnectionStringMaster == null)
                return null;

            var connection = new MySqlConnection(AppSettings.ConnectionStringMaster);
            connection.Open();
            return connection;
        }

        public static MySqlConnection GetSlaveConnection()
        {
            if (AppSettings.ConnectionStringSlave == null)
                return GetConnection();

            var connection = new MySqlConnection(AppSettings.ConnectionStringSlave);
            connection.Open();
            return connection;
        }
    }
}
