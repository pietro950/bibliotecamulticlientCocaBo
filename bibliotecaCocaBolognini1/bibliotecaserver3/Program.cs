using Microsoft.Data.Sqlite;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

const int Porta = 5000;

Database.Inizializza();

var server = new TcpListener(IPAddress.Any, Porta);
server.Start();

Console.WriteLine("Server Biblioteca avviato.");
Console.WriteLine($"Porta: {Porta}");
Console.WriteLine("Username prova: admin");
Console.WriteLine("Password prova: admin123");
Console.WriteLine();

while (true)
{
    TcpClient client = await server.AcceptTcpClientAsync();
    _ = GestisciClient(client);
}

async Task GestisciClient(TcpClient client)
{
    Console.WriteLine("Nuovo client collegato.");

    int? utenteId = null;

    try
    {
        using NetworkStream stream = client.GetStream();

        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            leaveOpen: true);

        using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(false),
            leaveOpen: true)
        {
            AutoFlush = true
        };

        while (true)
        {
            string? jsonRichiesta = await reader.ReadLineAsync();

            if (jsonRichiesta == null)
                break;

            Risposta risposta;

            try
            {
                Richiesta? richiesta =
                    JsonSerializer.Deserialize<Richiesta>(
                        jsonRichiesta);

                if (richiesta == null)
                {
                    risposta = Errore("Richiesta non valida.");
                }
                else
                {
                    risposta = GestisciRichiesta(
                        richiesta,
                        ref utenteId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                risposta = Errore("Errore interno del server.");
            }

            string jsonRisposta =
                JsonSerializer.Serialize(risposta);

            await writer.WriteLineAsync(jsonRisposta);
        }
    }
    catch
    {
        Console.WriteLine("Client disconnesso.");
    }
    finally
    {
        client.Close();
    }
}

Risposta GestisciRichiesta(
    Richiesta richiesta,
    ref int? utenteId)
{
    switch (richiesta.Azione.ToLower())
    {
        case "login":
            if (string.IsNullOrWhiteSpace(richiesta.Username) ||
                string.IsNullOrWhiteSpace(richiesta.Password))
            {
                return Errore("Username e password obbligatori.");
            }

            if (Database.Login(
                richiesta.Username,
                richiesta.Password,
                out int id))
            {
                utenteId = id;

                return Ok("Login effettuato.");
            }

            return Errore("Username o password errati.");

        case "libri":
            if (!ControllaLogin(utenteId, out Risposta? erroreLibri))
                return erroreLibri!;

            return new Risposta
            {
                Successo = true,
                Messaggio = "Libri caricati.",
                Dati = Database.GetLibri()
            };

        case "prestito":
            if (!ControllaLogin(utenteId, out Risposta? errorePrestito))
                return errorePrestito!;

            if (richiesta.LibroId == null)
                return Errore("Libro non specificato.");

            bool prestito = Database.PrestaLibro(
                utenteId!.Value,
                richiesta.LibroId.Value,
                out string messaggioPrestito);

            return new Risposta
            {
                Successo = prestito,
                Messaggio = messaggioPrestito
            };

        case "miei_prestiti":
            if (!ControllaLogin(utenteId, out Risposta? errorePrestiti))
                return errorePrestiti!;

            return new Risposta
            {
                Successo = true,
                Messaggio = "Prestiti caricati.",
                Dati = Database.GetPrestitiUtente(
                    utenteId!.Value)
            };

        case "restituzione":
            if (!ControllaLogin(utenteId, out Risposta? erroreRestituzione))
                return erroreRestituzione!;

            if (richiesta.PrestitoId == null)
                return Errore("Prestito non specificato.");

            bool restituito = Database.RestituisciLibro(
                utenteId!.Value,
                richiesta.PrestitoId.Value,
                out string messaggioRestituzione);

            return new Risposta
            {
                Successo = restituito,
                Messaggio = messaggioRestituzione
            };

        case "logout":
            utenteId = null;
            return Ok("Logout effettuato.");

        default:
            return Errore("Azione non riconosciuta.");
    }
}

bool ControllaLogin(
    int? utenteId,
    out Risposta? errore)
{
    if (utenteId == null)
    {
        errore = Errore("Devi prima effettuare il login.");
        return false;
    }

    errore = null;
    return true;
}

Risposta Ok(string messaggio)
{
    return new Risposta
    {
        Successo = true,
        Messaggio = messaggio
    };
}

Risposta Errore(string messaggio)
{
    return new Risposta
    {
        Successo = false,
        Messaggio = messaggio
    };
}

class Richiesta
{
    public string Azione { get; set; } = "";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public int? LibroId { get; set; }
    public int? PrestitoId { get; set; }
}

class Risposta
{
    public bool Successo { get; set; }
    public string Messaggio { get; set; } = "";
    public object? Dati { get; set; }
}

class Libro
{
    public int Id { get; set; }
    public string Titolo { get; set; } = "";
    public string Autore { get; set; } = "";
    public int CopieTotali { get; set; }
    public int CopieDisponibili { get; set; }
}

class Prestito
{
    public int Id { get; set; }
    public string TitoloLibro { get; set; } = "";
    public string Stato { get; set; } = "";
    public string DataPrestito { get; set; } = "";
    public string? DataRestituzione { get; set; }
}

static class Database
{
    private static readonly string ConnectionString =
        $"Data Source={DatabaseFile}";

    private static SqliteConnection Connessione()
    {
        var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        return connection;
    }

    public static void Inizializza()
    {
        using var connection = Connessione();
        using var command = connection.CreateCommand();

        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Utenti
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL UNIQUE,
                PasswordHash TEXT NOT NULL,
                Salt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Libri
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Titolo TEXT NOT NULL,
                Autore TEXT NOT NULL,
                CopieTotali INTEGER NOT NULL,
                CopieDisponibili INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Prestiti
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UtenteId INTEGER NOT NULL,
                LibroId INTEGER NOT NULL,
                Stato TEXT NOT NULL,
                DataPrestito TEXT NOT NULL,
                DataRestituzione TEXT NULL
            );
            """;

        command.ExecuteNonQuery();

        CreaUtenteAdmin(connection);
        CreaLibri(connection);
    }

    private static void CreaUtenteAdmin(
        SqliteConnection connection)
    {
        using var controllo = connection.CreateCommand();

        controllo.CommandText =
            "SELECT COUNT(*) FROM Utenti WHERE Username = 'admin'";

        long esiste = (long)controllo.ExecuteScalar()!;

        if (esiste > 0)
            return;

        CreaHash(
            "admin123",
            out string hash,
            out string salt);

        using var comando = connection.CreateCommand();

        comando.CommandText = """
            INSERT INTO Utenti
            (Username, PasswordHash, Salt)
            VALUES ('admin', $hash, $salt);
            """;

        comando.Parameters.AddWithValue("$hash", hash);
        comando.Parameters.AddWithValue("$salt", salt);

        comando.ExecuteNonQuery();
    }

    private static void CreaLibri(
        SqliteConnection connection)
    {
        using var controllo = connection.CreateCommand();

        controllo.CommandText =
            "SELECT COUNT(*) FROM Libri";

        long numeroLibri = (long)controllo.ExecuteScalar()!;

        if (numeroLibri > 0)
            return;

        InserisciLibro(
            connection,
            "Il nome della rosa",
            "Umberto Eco",
            3);

        InserisciLibro(
            connection,
            "1984",
            "George Orwell",
            2);

        InserisciLibro(
            connection,
            "I promessi sposi",
            "Alessandro Manzoni",
            4);
    }

    private static void InserisciLibro(
        SqliteConnection connection,
        string titolo,
        string autore,
        int copie)
    {
        using var comando = connection.CreateCommand();

        comando.CommandText = """
            INSERT INTO Libri
            (Titolo, Autore, CopieTotali, CopieDisponibili)
            VALUES ($titolo, $autore, $copie, $copie);
            """;

        comando.Parameters.AddWithValue("$titolo", titolo);
        comando.Parameters.AddWithValue("$autore", autore);
        comando.Parameters.AddWithValue("$copie", copie);

        comando.ExecuteNonQuery();
    }

    public static bool Login(
        string username,
        string password,
        out int utenteId)
    {
        utenteId = 0;

        using var connection = Connessione();
        using var comando = connection.CreateCommand();

        comando.CommandText = """
            SELECT Id, PasswordHash, Salt
            FROM Utenti
            WHERE Username = $username;
            """;

        comando.Parameters.AddWithValue("$username", username);

        using var reader = comando.ExecuteReader();

        if (!reader.Read())
            return false;

        utenteId = reader.GetInt32(0);

        string hashSalvato = reader.GetString(1);
        string saltSalvato = reader.GetString(2);

        return VerificaPassword(
            password,
            hashSalvato,
            saltSalvato);
    }

    public static List<Libro> GetLibri()
    {
        var libri = new List<Libro>();

        using var connection = Connessione();
        using var comando = connection.CreateCommand();

        comando.CommandText = """
            SELECT Id, Titolo, Autore,
                   CopieTotali, CopieDisponibili
            FROM Libri
            ORDER BY Titolo;
            """;

        using var reader = comando.ExecuteReader();

        while (reader.Read())
        {
            libri.Add(new Libro
            {
                Id = reader.GetInt32(0),
                Titolo = reader.GetString(1),
                Autore = reader.GetString(2),
                CopieTotali = reader.GetInt32(3),
                CopieDisponibili = reader.GetInt32(4)
            });
        }

        return libri;
    }

    public static bool PrestaLibro(
        int utenteId,
        int libroId,
        out string messaggio)
    {
        using var connection = Connessione();
        using var transaction = connection.BeginTransaction();

        using var controllo = connection.CreateCommand();
        controllo.Transaction = transaction;
        controllo.CommandText = """
            SELECT CopieDisponibili
            FROM Libri
            WHERE Id = $libroId;
            """;

        controllo.Parameters.AddWithValue("$libroId", libroId);

        object? risultato = controllo.ExecuteScalar();

        if (risultato == null)
        {
            messaggio = "Libro non trovato.";
            return false;
        }

        int copieDisponibili = Convert.ToInt32(risultato);

        if (copieDisponibili <= 0)
        {
            messaggio = "Libro non disponibile.";
            return false;
        }

        using var inserimento = connection.CreateCommand();
        inserimento.Transaction = transaction;
        inserimento.CommandText = """
            INSERT INTO Prestiti
            (UtenteId, LibroId, Stato, DataPrestito)
            VALUES
            ($utenteId, $libroId, 'Attivo', $data);
            """;

        inserimento.Parameters.AddWithValue("$utenteId", utenteId);
        inserimento.Parameters.AddWithValue("$libroId", libroId);
        inserimento.Parameters.AddWithValue(
            "$data",
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        inserimento.ExecuteNonQuery();

        using var aggiornamento = connection.CreateCommand();
        aggiornamento.Transaction = transaction;
        aggiornamento.CommandText = """
            UPDATE Libri
            SET CopieDisponibili = CopieDisponibili - 1
            WHERE Id = $libroId;
            """;

        aggiornamento.Parameters.AddWithValue("$libroId", libroId);
        aggiornamento.ExecuteNonQuery();

        transaction.Commit();

        messaggio = "Prestito confermato.";
        return true;
    }

    public static List<Prestito> GetPrestitiUtente(
        int utenteId)
    {
        var prestiti = new List<Prestito>();

        using var connection = Connessione();
        using var comando = connection.CreateCommand();

        comando.CommandText = """
            SELECT Prestiti.Id,
                   Libri.Titolo,
                   Prestiti.Stato,
                   Prestiti.DataPrestito,
                   Prestiti.DataRestituzione
            FROM Prestiti
            INNER JOIN Libri
                ON Libri.Id = Prestiti.LibroId
            WHERE Prestiti.UtenteId = $utenteId
            ORDER BY Prestiti.Id DESC;
            """;

        comando.Parameters.AddWithValue("$utenteId", utenteId);

        using var reader = comando.ExecuteReader();

        while (reader.Read())
        {
            prestiti.Add(new Prestito
            {
                Id = reader.GetInt32(0),
                TitoloLibro = reader.GetString(1),
                Stato = reader.GetString(2),
                DataPrestito = reader.GetString(3),
                DataRestituzione = reader.IsDBNull(4)
                    ? null
                    : reader.GetString(4)
            });
        }

        return prestiti;
    }

    public static bool RestituisciLibro(
        int utenteId,
        int prestitoId,
        out string messaggio)
    {
        using var connection = Connessione();
        using var transaction = connection.BeginTransaction();

        using var cerca = connection.CreateCommand();
        cerca.Transaction = transaction;
        cerca.CommandText = """
            SELECT LibroId
            FROM Prestiti
            WHERE Id = $prestitoId
              AND UtenteId = $utenteId
              AND Stato = 'Attivo';
            """;

        cerca.Parameters.AddWithValue("$prestitoId", prestitoId);
        cerca.Parameters.AddWithValue("$utenteId", utenteId);

        object? risultato = cerca.ExecuteScalar();

        if (risultato == null)
        {
            messaggio = "Prestito non trovato.";
            return false;
        }

        int libroId = Convert.ToInt32(risultato);

        using var aggiornaPrestito = connection.CreateCommand();
        aggiornaPrestito.Transaction = transaction;
        aggiornaPrestito.CommandText = """
            UPDATE Prestiti
            SET Stato = 'Restituito',
                DataRestituzione = $data
            WHERE Id = $prestitoId;
            """;

        aggiornaPrestito.Parameters.AddWithValue(
            "$prestitoId",
            prestitoId);

        aggiornaPrestito.Parameters.AddWithValue(
            "$data",
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        aggiornaPrestito.ExecuteNonQuery();

        using var aggiornaLibro = connection.CreateCommand();
        aggiornaLibro.Transaction = transaction;
        aggiornaLibro.CommandText = """
            UPDATE Libri
            SET CopieDisponibili = CopieDisponibili + 1
            WHERE Id = $libroId;
            """;

        aggiornaLibro.Parameters.AddWithValue("$libroId", libroId);
        aggiornaLibro.ExecuteNonQuery();

        transaction.Commit();

        messaggio = "Restituzione completata.";
        return true;
    }

    private static void CreaHash(
        string password,
        out string hash,
        out string salt)
    {
        byte[] saltBytes =
            RandomNumberGenerator.GetBytes(16);

        byte[] hashBytes =
            Rfc2898DeriveBytes.Pbkdf2(
                password,
                saltBytes,
                100000,
                HashAlgorithmName.SHA256,
                32);

        salt = Convert.ToBase64String(saltBytes);
        hash = Convert.ToBase64String(hashBytes);
    }

    private static bool VerificaPassword(
        string password,
        string hashSalvato,
        string saltSalvato)
    {
        byte[] salt =
            Convert.FromBase64String(saltSalvato);

        byte[] hashAttuale =
            Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                100000,
                HashAlgorithmName.SHA256,
                32);

        byte[] hashPrecedente =
            Convert.FromBase64String(hashSalvato);

        return CryptographicOperations.FixedTimeEquals(
            hashAttuale,
            hashPrecedente);
    }
}
