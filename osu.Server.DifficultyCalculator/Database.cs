// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu/master/LICENCE

using System.Data;
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

        public MySqlDataReader RunQuery(string sqlString, params MySqlParameter[] parameters)
        {
            MySqlConnection m = getConnection();
            MySqlCommand c = m.CreateCommand();
            if (parameters != null)
                c.Parameters.AddRange(parameters);
            c.CommandText = sqlString;
            c.CommandTimeout = 180;
            return c.ExecuteReader(CommandBehavior.CloseConnection);
        }

        public object RunQueryOne(string sqlString, params MySqlParameter[] parameters)
        {
            using (MySqlConnection m = getConnection())
            {
                using (MySqlCommand c = m.CreateCommand())
                {
                    if (parameters != null)
                        c.Parameters.AddRange(parameters);
                    c.CommandText = sqlString;
                    c.CommandTimeout = 180;
                    return c.ExecuteScalar();
                }
            }
        }

        public int RunNonQuery(string sqlString, params MySqlParameter[] parameters)
        {
            using (MySqlConnection m = getConnection())
            {
                using (MySqlCommand c = m.CreateCommand())
                {
                    if (parameters != null)
                        c.Parameters.AddRange(parameters);
                    c.CommandText = sqlString;
                    c.CommandTimeout = 180;
                    return c.ExecuteNonQuery();
                }
            }
        }

        public DataSet RunDataset(string sqlString, params MySqlParameter[] parameters)
        {
            using (MySqlConnection m = getConnection())
                return MySqlHelper.ExecuteDataset(m, sqlString, parameters);
        }

        private MySqlConnection getConnection()
        {
            var connection = new MySqlConnection(connectionString);
            connection.Open();
            return connection;
        }
    }
}
