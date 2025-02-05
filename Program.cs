using ArgParseLib;
using Microsoft.Data.Sqlite;
using System.Data;
using System.Numerics;
using System.Text;

namespace HashMapper
{
    internal class Program
    {
        static readonly string DEFAULT_CHAR_SET_TEXT = "abcdefghijklmnopqrstuvwxyz1234567890`~-_=+[]{}\\/|;:'\",.<>?!@#$%^&*()";
        static readonly string DEFAULT_TABLE_NAME = "hasher";

        static int Main(string[] args)
        {
            string charSetFilePath = "";
            string dbFilePath = "";
            bool displayHelp = false;
            bool resetDb = false;
            string tableName = DEFAULT_TABLE_NAME;
            BigInteger minIdx = BigInteger.Zero;
            BigInteger maxIdx = 100;

            OptionParam[] options =
            {
                new Option("-c|--char-set", "filepath", "Path to file that contains all char-set for generated hashes. Leading and trailing whitespace is ignored.", (value) =>
                {
                    charSetFilePath = value;
                }),
                new Option("-d|--db-path", "filepath", "Path to SQLite .db file where hashes will be stored.", (value) =>
                {
                    dbFilePath = value;
                }),
                new Flag("-h|--help", "Display these usage instructions.", () =>
                {
                    displayHelp = true;
                }),
                new Flag("-r|--reset", "Reset the database of stored hashes.", () =>
                {
                    resetDb = true;
                }),
                new Option("-t|--table-name", "tableName", "Custom table name for storing hashes for a particular charset", (value) =>
                {
                    tableName = value;
                }),
                new Option("-m|--min", "minValue", "Lower bound index to start generating hashes", (value) =>
                {
                    minIdx = BigInteger.Parse(value);
                }),
                new Option("-M|--max", "maxValue", "Upper bound index to start generating hashes", (value) =>
                {
                    maxIdx = BigInteger.Parse(value);
                })
            };

            ArgParser parser = new ArgParser(options, "hasher");
            parser.Parse(args);

            if (displayHelp)
            {
                parser.Usage();
                return 0;
            }

            if (minIdx < 0)
            {
                Console.Error.WriteLine("Min Index is less than 0, this is not allowed.");
                return -1;
            }
            if(minIdx >= maxIdx)
            {
                Console.Error.WriteLine("Min Index is greater than or equal to Max Index, this is not allowed.");
                return -1;
            }

            string rawCharSetText;

            if (!string.IsNullOrEmpty(charSetFilePath))
            {
                Console.WriteLine($"Will read characters from {charSetFilePath}");
            }

            if (!string.IsNullOrEmpty(dbFilePath))
            {
                Console.WriteLine($"Will store hashes in {dbFilePath}");
            }

            if (!File.Exists(charSetFilePath))
            {
                if (string.IsNullOrEmpty(charSetFilePath))
                {
                    Console.Write("No charset file provided.");
                } else
                {
                    Console.Write(Path.GetFileName(charSetFilePath) + " does not exist.");
                }

                Console.WriteLine(" Using default charset.");

                rawCharSetText = DEFAULT_CHAR_SET_TEXT;
            } else
            {
                rawCharSetText = File.ReadAllText(charSetFilePath);
            }

            if (!File.Exists(dbFilePath))
            {
                if (string.IsNullOrEmpty(dbFilePath))
                {
                    Console.Error.WriteLine("No database file provided.");
                    return -1;
                }
                else
                {
                    Console.Write(Path.GetFileName(dbFilePath) + " does not exist.");
                }

                Console.WriteLine(" Will create new database file at " + Path.GetFullPath(dbFilePath));
                resetDb = true;
            }

            string conString = new SqliteConnectionStringBuilder()
            {
                DataSource = dbFilePath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();

            SqliteConnection con = new SqliteConnection(conString);

            if (string.IsNullOrEmpty(rawCharSetText))
            {
                rawCharSetText = DEFAULT_CHAR_SET_TEXT;
            }

            char[] charset = new HashSet<char>(rawCharSetText.Trim().ToCharArray()).ToArray();

            Array.Sort(charset);

            Console.WriteLine($"Char set loaded with {charset.Length} characters:\n");
            Console.Write(string.Concat(charset));

            Console.WriteLine();

            con.Open();

            if (resetDb)
            {
                setupDb(con, charset.ToArray(), tableName);
            }

            Console.WriteLine("First character sequence: " + DigitConverter.convertFromBase10(minIdx, charset));

            while (minIdx < maxIdx)
            {
                if (minIdx % 500 == 0)
                {
                    Console.WriteLine(minIdx);
                }
                string input = DigitConverter.convertFromBase10(minIdx, charset);
                insertHash(con, ((int)minIdx), input, generateHash(input), tableName);
                minIdx++;
            }

            con.Close();
            return 0;
        }

        private static string generateHash(string input)
        {
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);

                return Convert.ToHexString(hashBytes); // .NET 5 +

                // Convert the byte array to hexadecimal string prior to .NET 5
                // StringBuilder sb = new System.Text.StringBuilder();
                // for (int i = 0; i < hashBytes.Length; i++)
                // {
                //     sb.Append(hashBytes[i].ToString("X2"));
                // }
                // return sb.ToString();
            }
        }

        private static void dropAllTables(SqliteConnection con)
        {
            var command = con.CreateCommand();

            List<string> tables = new();

            command.CommandText =
            @"
                SELECT tbl_name FROM sqlite_master WHERE type=""table"";
            ";
            var reader = command.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
            foreach (var table in tables)
            { 
                command = con.CreateCommand();
                command.CommandText = $"DROP TABLE {table};";
                command.ExecuteNonQuery();
            }
        }

        private static void createHashTable(SqliteConnection con, string tableName, string charset, string last_index)
        {
            Console.WriteLine("Creating table " +  tableName);

            var command = con.CreateCommand();
            command.CommandText =
            $@"
            DROP TABLE IF EXISTS {tableName};
            CREATE TABLE {tableName} (
	           ""id""	INTEGER NOT NULL UNIQUE,
	            ""text""	TEXT NOT NULL UNIQUE,
	            ""md5_hash""	TEXT NOT NULL,
	            PRIMARY KEY(""id"")
            );
            ";
            command.ExecuteNonQuery();

            command = con.CreateCommand();
            command.CommandText =
            @"
            INSERT INTO hash_tables VALUES ($t, $cs, $li);
            ";
            command.Parameters.AddWithValue("$t", tableName);
            command.Parameters.AddWithValue("$cs", charset);
            command.Parameters.AddWithValue("$li", last_index);
            command.ExecuteNonQuery();
        }

        private static void createMetaTable(SqliteConnection con)
        {
            var command = con.CreateCommand();
            command.CommandText =
            @"
            DROP TABLE IF EXISTS hash_tables;
            CREATE TABLE ""hash_tables"" (
                    ""hash_table_name""	TEXT NOT NULL UNIQUE,
                    ""charset""	TEXT NOT NULL UNIQUE,
                    ""last_index""	TEXT NOT NULL,
                    PRIMARY KEY(""hash_table_name"")
            );
            ";
            command.ExecuteNonQuery();
        }

        private static void setupDb(SqliteConnection con, char[] charset, string tableName)
        {
            Console.WriteLine("Initializing database...");

            dropAllTables(con);
            createMetaTable(con);
            createHashTable(con, tableName, string.Concat(charset), "0");
        }

        private static void insertHash(SqliteConnection con, int id, string input, string hash, string tableName)
        {
            var command = con.CreateCommand();
            command.CommandText = $"INSERT INTO {tableName} VALUES ($id, $input, $hash);";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$input", input);
            command.Parameters.AddWithValue("$hash", hash);
            command.ExecuteNonQuery();
        }
    }
}
