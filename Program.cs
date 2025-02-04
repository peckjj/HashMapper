using ArgParseLib;
using Microsoft.Data.Sqlite;

namespace HashMapper
{
    internal class Program
    {
        static readonly string DEFAULT_CHAR_SET_TEXT = "abcdefghijklmnopqrstuvwxyz1234567890`~-_=+[]{}\\/|;:'\",.<>?!@#$%^&*()";

        static int Main(string[] args)
        {
            string charSetFilePath = "";
            string dbFilePath = "";
            bool displayHelp = false;
            bool resetDb = false;

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
                })
            };

            ArgParser parser = new ArgParser(options, "hasher");
            parser.Parse(args);

            if (displayHelp)
            {
                parser.Usage();
                return 0;
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

            using (var connection = new SqliteConnection(conString))
            {
                if (resetDb)
                {
                    setupDb(connection);
                }

                connection.Open();

                if (string.IsNullOrEmpty(rawCharSetText))
                {
                    rawCharSetText = DEFAULT_CHAR_SET_TEXT;
                }

                HashSet<char> charset = new HashSet<char>(rawCharSetText.Trim().ToCharArray());

                Console.WriteLine($"Char set loaded with {charset.Count} characters:\n");
                foreach (char c in charset)
                {
                    Console.Write(c);
                }

                Console.WriteLine();
            }

            return 0;
        }

        private static void setupDb(SqliteConnection connection)
        {
            using (connection)
            {
                connection.Open();

                Console.WriteLine("Initializing database...");
                var command = connection.CreateCommand();
                command.CommandText =
                @"
                    DROP TABLE IF EXISTS hashes;
                    CREATE TABLE ""hashes"" (
	                    ""text""	TEXT NOT NULL UNIQUE,
	                    ""md5_hash""	TEXT NOT NULL,
	                    PRIMARY KEY(""text"")
                    );
                ";

                Console.WriteLine(command.CommandText);

                command.ExecuteNonQuery();
            }
        }

        private static void insertHash(SqliteConnection connection, string input, string hash)
        {
            if (connection.State != System.Data.ConnectionState.Open)
            {
                throw new Exception("Database connection is not opened.");
            }

            var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO hashes VALUES ($input, $hash);";
            command.Parameters.AddWithValue("$input", input);
            command.Parameters.AddWithValue("$hash", hash);
            command.ExecuteNonQuery();
        }
    }
}
