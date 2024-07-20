using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Npgsql;

namespace OpenDirectoryDownloader
{
    public class PostgresHandler
    {
        private readonly string _connectionString;

        public PostgresHandler(string connectionString)
        {
            _connectionString = connectionString;
        }

        private string NormalizeExtension(string extension)
        {
            // Convert to lowercase
            extension = extension.ToLower();
            // Remove trailing underscores, spaces, or other non-alphanumeric characters
            extension = Regex.Replace(extension, @"[^a-z0-9]+$", "");
            return extension;
        }

        public async Task InsertUrls(IEnumerable<string> urls)
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            foreach (var url in urls)
            {
                using var cmd = new NpgsqlCommand(
                "INSERT INTO site_inventory (url, file_name, file_extension, last_modified) " +
                "VALUES (@url, @fileName, @fileExtension, @lastModified) " +
                "ON CONFLICT (url) DO NOTHING", connection);

                var uri = new Uri(url);
                var fileName = Path.GetFileName(uri.LocalPath);
                var fileExtension = Path.GetExtension(fileName);

                // Normalize the file extension
                var normalizedFileExtension = NormalizeExtension(fileExtension);

                cmd.Parameters.AddWithValue("url", url);
                cmd.Parameters.AddWithValue("fileName", fileName);
                cmd.Parameters.AddWithValue("fileExtension", normalizedFileExtension);
                cmd.Parameters.AddWithValue("lastModified", DateTime.UtcNow);

                await cmd.ExecuteNonQueryAsync();
            }
        }
    }
}
