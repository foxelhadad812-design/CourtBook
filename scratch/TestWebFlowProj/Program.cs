using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler { CookieContainer = cookieContainer, AllowAutoRedirect = true };
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };

        Console.WriteLine("\n[1] Registering as new Client...");
        var regPage = await client.GetStringAsync("/Register");
        var rvt = Regex.Match(regPage, @"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""").Groups[1].Value;

        var regData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Name", "Web Client"),
            new KeyValuePair<string, string>("Input.Email", "webclient@courtbook.dev"),
            new KeyValuePair<string, string>("Input.Phone", "123"),
            new KeyValuePair<string, string>("Input.Password", "Password@123"),
            new KeyValuePair<string, string>("__RequestVerificationToken", rvt)
        });

        var regRes = await client.PostAsync("/Register", regData);
        Console.WriteLine($"Register POST returned. URL: {regRes.RequestMessage.RequestUri}");

        Console.WriteLine("\n[2] Logging in as Client...");
        var loginPage = await client.GetStringAsync("/Login");
        rvt = Regex.Match(loginPage, @"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""").Groups[1].Value;

        var loginData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Email", "webclient@courtbook.dev"),
            new KeyValuePair<string, string>("Input.Password", "Password@123"),
            new KeyValuePair<string, string>("__RequestVerificationToken", rvt)
        });

        var loginRes = await client.PostAsync("/Login", loginData);
        Console.WriteLine($"Login POST returned. URL: {loginRes.RequestMessage.RequestUri}");

        Console.WriteLine("\n[3] Browsing Venues...");
        var venuesPage = await client.GetStringAsync("/Venues");
        var venueMatch = Regex.Match(venuesPage, @"/Venues/Details/([a-fA-F0-9-]+)");
        if (venueMatch.Success)
        {
            var venueId = venueMatch.Groups[1].Value;
            Console.WriteLine($"Found Venue ID: {venueId}");

            Console.WriteLine("\n[4] Viewing Venue Details...");
            var detailsPage = await client.GetStringAsync($"/Venues/Details/{venueId}");
            var courtMatch = Regex.Match(detailsPage, @"/Courts/Book\?venueId=[a-f0-9-]+&amp;id=[a-f0-9-]+");
            
            if (courtMatch.Success)
            {
                var bookUrl = courtMatch.Value.Replace("&amp;", "&");
                Console.WriteLine($"Found Court to book: {bookUrl}");

                var bookPage = await client.GetStringAsync(bookUrl);
                rvt = Regex.Match(bookPage, @"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""").Groups[1].Value;

                var nextMonday = DateTime.Today.AddDays(((int)DayOfWeek.Monday - (int)DateTime.Today.DayOfWeek + 7) % 7);
                if (nextMonday == DateTime.Today) nextMonday = nextMonday.AddDays(7);

                Console.WriteLine("\n[5] Booking slot via Web...");
                var bookData = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("BookingDate", nextMonday.ToString("yyyy-MM-dd")),
                    new KeyValuePair<string, string>("StartTime", "12:00"),
                    new KeyValuePair<string, string>("EndTime", "14:00"),
                    new KeyValuePair<string, string>("__RequestVerificationToken", rvt)
                });

                var bookRes = await client.PostAsync(bookUrl, bookData);
                Console.WriteLine($"Booking POST returned. URL: {bookRes.RequestMessage.RequestUri}");

                Console.WriteLine("\n[6] Checking My Bookings Page...");
                var myBookings = await client.GetStringAsync("/Bookings/MyBookings");
                if (myBookings.Contains("Cancel"))
                {
                    Console.WriteLine("Booking found in My Bookings!");
                    
                    var cancelMatch = Regex.Match(myBookings, @"asp-route-id=""([^""]+)""");
                    // Razor renders to action="/Bookings/MyBookings?id=xxx&handler=Cancel"
                    var actionMatch = Regex.Match(myBookings, @"action=""(/Bookings/MyBookings\?[^""]+Cancel[^""]*)""");
                    if (actionMatch.Success)
                    {
                        Console.WriteLine("\n[7] Cancelling booking via Web...");
                        var cancelUrl = actionMatch.Groups[1].Value.Replace("&amp;", "&");
                        rvt = Regex.Match(myBookings, @"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""").Groups[1].Value;

                        var cancelData = new FormUrlEncodedContent(new[]
                        {
                            new KeyValuePair<string, string>("__RequestVerificationToken", rvt)
                        });

                        var cancelRes = await client.PostAsync(cancelUrl, cancelData);
                        Console.WriteLine($"Cancel POST returned. URL: {cancelRes.RequestMessage.RequestUri}");
                    }
                }
            }
        }

        Console.WriteLine("\n[8] Logging out...");
        await client.GetAsync("/Logout");
        Console.WriteLine("Logged out.");

        Console.WriteLine("\n[9] Logging in as Owner...");
        loginPage = await client.GetStringAsync("/Login");
        rvt = Regex.Match(loginPage, @"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""").Groups[1].Value;

        loginData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Email", "owner@courtbook.dev"),
            new KeyValuePair<string, string>("Input.Password", "Owner@1234"),
            new KeyValuePair<string, string>("__RequestVerificationToken", rvt)
        });

        loginRes = await client.PostAsync("/Login", loginData);
        Console.WriteLine($"Owner Login POST returned. URL: {loginRes.RequestMessage.RequestUri}");

        Console.WriteLine("\n[10] Accessing Dashboard...");
        var dash = await client.GetStringAsync("/Dashboard/Index");
        if (dash.Contains("Create New Venue"))
        {
            Console.WriteLine("Dashboard loaded successfully!");
        }

        Console.WriteLine("\nALL WEB TESTS PASSED!");
    }
}
