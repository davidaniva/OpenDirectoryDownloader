﻿using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Shared.Models;
using System.Diagnostics;

namespace OpenDirectoryDownloader.Calibre;

public static class CalibreParser
{
	public static Version ParseVersion(string versionString)
	{
		if (versionString.Contains('/'))
		{
			string[] splitted = versionString.Split('/');

			return Version.Parse(splitted.Last());
		}

		if (versionString.Contains(' '))
		{
			string[] splitted = versionString.Split(' ');

			return Version.Parse(splitted.Last());
		}

		return Version.Parse(versionString);
	}

	public static async Task ParseCalibre(HttpClient httpClient, Uri calibreRootUri, WebDirectory webDirectory, Version version, CancellationToken cancellationToken)
	{
		try
		{
			Console.WriteLine("Retrieving libraries...");
			Program.Logger.Information("Retrieving libraries...");

			if (version.Major < 3)
			{
				Console.WriteLine($"Calibre {version} is not supported..");
				return;
			}

			HttpResponseMessage httpResponseMessage = await httpClient.GetAsync(new Uri(calibreRootUri, "./interface-data/update"), cancellationToken);
			httpResponseMessage.EnsureSuccessStatusCode();

			string updateResultJson = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken);

			CalibreUpdate.CalibreUpdate calibreUpdate = CalibreUpdate.CalibreUpdate.FromJson(updateResultJson);

			Console.WriteLine($"Retrieved {calibreUpdate.LibraryMap.Count} libraries");
			Program.Logger.Information($"Retrieved {calibreUpdate.LibraryMap.Count} libraries");

			foreach (KeyValuePair<string, string> library in calibreUpdate.LibraryMap)
			{
				Console.WriteLine($"Retrieving metadata of books for library {library.Value}...");
				Program.Logger.Information($"Retrieving metadata of books for library {library.Value}...");

				WebDirectory libraryWebDirectory = new(webDirectory)
				{
					Url = $"{calibreRootUri}/#library_id={library.Key}&panel=book_list",
					Name = library.Value,
					Parser = "Calibre"
				};

				webDirectory.Subdirectories.Add(libraryWebDirectory);

				Uri libraryMetadataUri = new(calibreRootUri, $"./interface-data/books-init?library_id={library.Key}&num=999999999");

				httpResponseMessage = await httpClient.GetAsync(libraryMetadataUri, cancellationToken);
				httpResponseMessage.EnsureSuccessStatusCode();

				string libraryResultJson = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken);


				libraryWebDirectory.Files.Add(new WebFile
				{
					FileName = "LibraryMetadata.json",
					FileSize = libraryResultJson.Length,
					Url = libraryMetadataUri.ToString()
				});

				CalibreResult.CalibreResult libraryResult = CalibreResult.CalibreResult.FromJson(libraryResultJson);

				Console.WriteLine($"Retrieved metadata of {libraryResult.Metadata.Count} books for library {library.Value}");
				Program.Logger.Information($"Retrieved metadata of {libraryResult.Metadata.Count} books for library {library.Value}");

				Console.WriteLine($"Parsing info of {libraryResult.Metadata.Count} books for library {library.Value}...");
				Program.Logger.Information($"Parsing info of {libraryResult.Metadata.Count} books for library {library.Value}...");

				int booksToIndex = libraryResult.Metadata.Count;
				int booksIndexed = 0;

				Stopwatch stopwatch = Stopwatch.StartNew();

				//RateLimiter rateLimiter = new RateLimiter(100, TimeSpan.FromSeconds(30));

				Parallel.ForEach(libraryResult.Metadata.AsParallel().AsOrdered(), new ParallelOptions { MaxDegreeOfParallelism = 100 }, (book) =>
				{
					// NOT async, else it will continue with the rest of the code..
					// TODO: Need a nice fix which respects MaxDegreeOfParallelism

					//rateLimiter.RateLimit().Wait();
					GetBookInfo(httpClient, calibreRootUri, library, libraryWebDirectory, book);

					int newBooksIndexed = Interlocked.Increment(ref booksIndexed);

					if (newBooksIndexed % 100 == 0 && stopwatch.Elapsed > TimeSpan.FromSeconds(5))
					{
						Program.Logger.Warning($"Parsing books at {100 * ((decimal)newBooksIndexed / booksToIndex):F1}% ({newBooksIndexed}/{booksToIndex})");
						stopwatch.Restart();
					}
				});

				Console.WriteLine($"Parsed info of {libraryResult.Metadata.Count} books for library {library.Value}");
				Program.Logger.Information($"Parsed info of {libraryResult.Metadata.Count} books for library {library.Value}");
			}
		}
		catch (Exception ex)
		{
			Program.Logger.Error(ex, "Error parsing Calibre");
			webDirectory.Error = true;
		}
	}

	private static void GetBookInfo(HttpClient httpClient, Uri calibreRootUri, KeyValuePair<string, string> library, WebDirectory libraryWebDirectory, KeyValuePair<string, CalibreResult.Metadatum> book)
	{
		Program.Logger.Debug($"Retrieving info for book [{book.Key}]: {book.Value.Title}...");

		WebDirectory bookWebDirectory = new(libraryWebDirectory)
		{
			Url = new Uri(calibreRootUri, $"./#book_id={book.Key}&library_id={library.Key}&panel=book_list").ToString(),
			Name = book.Value.Title,
			Parser = "Calibre"
		};

		try
		{
			string coverUrl = new Uri(calibreRootUri, $"./get/cover/{book.Key}/{library.Key}").ToString();

			// Not anymore, makes not much sence and costs a ton of time
			//MediaTypeHeaderValue contentTypeHeader = await UrlHeaderInfoHelper.GetContentTypeAsync(httpClient, coverUrl);

			//string coverFileName = "cover";

			//if (contentTypeHeader != null)
			//{
			//    switch (contentTypeHeader.MediaType)
			//    {
			//        case "image/jpeg":
			//            coverFileName += ".jpg";
			//            break;
			//        default:
			//            coverFileName += ".unknown";
			//            break;
			//    }
			//}

			//bookWebDirectory.Files.Add(new WebFile
			//{
			//    Url = coverUrl,
			//    FileName = coverFileName,
			//    FileSize = await UrlHeaderInfoHelper.GetUrlFileSizeAsync(httpClient, coverUrl) ?? 0
			//});

			bookWebDirectory.Files.Add(new WebFile
			{
				Url = coverUrl,
				FileName = "cover.jpg",
				FileSize = 0
			});

			foreach (string format in book.Value.Formats)
			{
				bookWebDirectory.Files.Add(new WebFile
				{
					Url = new Uri(calibreRootUri, $"./get/{format.ToUpperInvariant()}/{book.Key}/{library.Key}").ToString(),
					FileName = $"{PathHelper.GetValidPath(book.Value.Title)} - {PathHelper.GetValidPath(book.Value.AuthorSort)}.{format.ToLowerInvariant()}",
					FileSize = book.Value.FormatSizes.ContainsKey(format) ? book.Value.FormatSizes[format] : 0
				});
			}

			libraryWebDirectory.Subdirectories.Add(bookWebDirectory);

			Program.Logger.Debug($"Retrieved info for book [{book.Key}]: {book.Value.Title}");
		}
		catch (Exception ex)
		{
			Program.Logger.Debug(ex, $"Error processing book {book.Key}");
			bookWebDirectory.Error = true;
		}
	}
}
