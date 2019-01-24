// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using MySql.Data.MySqlClient;

namespace osu.Server.DifficultyCalculator
{
    public class Database
    {
        private readonly string connectionString;

        public Database(string connectionString)
        {
            this.connectionString = connectionString;
        }

        public Context GetContext()
            => new Context(AppSettings.UseDatabase ? getConnection() : null);

        public T Perform<T>(Func<MySqlConnection, T> action)
        {
            using (var context = GetContext())
                return context.Perform(action);
        }

        public void Perform(Action<MySqlConnection> action)
        {
            using (var context = GetContext())
                context.Perform(action);
        }

        private MySqlConnection getConnection()
        {
            var connection = new MySqlConnection(connectionString);
            connection.Open();
            return connection;
        }

        public struct Context : IDisposable
        {
            private readonly MySqlConnection connection;

            public Context(MySqlConnection connection)
            {
                this.connection = connection;
            }

            public T Perform<T>(Func<MySqlConnection, T> action)
             => connection == null ? default : action(connection);

            public void Perform(Action<MySqlConnection> action)
            {
                if (connection != null)
                    action(connection);
            }

            public void Dispose()
            {
                connection?.Dispose();
            }
        }
    }
}
