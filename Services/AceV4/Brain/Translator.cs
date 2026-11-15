using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nimbus.AceV4.Brain
{
    /// <summary>
    /// Translates words and phrases. About 110 everyday phrases in Spanish, French, German, Italian and Portuguese
    /// (and some Japanese) work offline; anything else, and 25+ other languages, come from the free MyMemory
    /// translation service when the internet is on. Also works backwards: "what does gracias mean".
    /// </summary>
    public static class Translator
    {
        public sealed record Result(string English, string Language, string Translation, string Note, bool Offline);

        /// <summary>Language names people say → (proper name, MyMemory code).</summary>
        public static readonly Dictionary<string, (string Name, string Code)> Languages = new(StringComparer.OrdinalIgnoreCase)
        {
            ["spanish"] = ("Spanish", "es"), ["espanol"] = ("Spanish", "es"), ["español"] = ("Spanish", "es"), ["mexican"] = ("Spanish", "es"),
            ["french"] = ("French", "fr"), ["german"] = ("German", "de"), ["italian"] = ("Italian", "it"),
            ["portuguese"] = ("Portuguese", "pt"), ["brazilian"] = ("Portuguese", "pt"), ["japanese"] = ("Japanese", "ja"),
            ["chinese"] = ("Chinese", "zh-CN"), ["mandarin"] = ("Chinese", "zh-CN"), ["korean"] = ("Korean", "ko"),
            ["russian"] = ("Russian", "ru"), ["arabic"] = ("Arabic", "ar"), ["hindi"] = ("Hindi", "hi"), ["dutch"] = ("Dutch", "nl"),
            ["swedish"] = ("Swedish", "sv"), ["norwegian"] = ("Norwegian", "no"), ["danish"] = ("Danish", "da"), ["finnish"] = ("Finnish", "fi"),
            ["polish"] = ("Polish", "pl"), ["turkish"] = ("Turkish", "tr"), ["greek"] = ("Greek", "el"), ["hebrew"] = ("Hebrew", "he"),
            ["vietnamese"] = ("Vietnamese", "vi"), ["thai"] = ("Thai", "th"), ["indonesian"] = ("Indonesian", "id"), ["ukrainian"] = ("Ukrainian", "uk"),
            ["latin"] = ("Latin", "la"), ["irish"] = ("Irish", "ga"), ["welsh"] = ("Welsh", "cy"), ["tagalog"] = ("Filipino", "tl"), ["filipino"] = ("Filipino", "tl"),
            ["czech"] = ("Czech", "cs"), ["hungarian"] = ("Hungarian", "hu"), ["romanian"] = ("Romanian", "ro"), ["swahili"] = ("Swahili", "sw"),
            ["english"] = ("English", "en"),
        };

        private static readonly string[] Five = { "es", "fr", "de", "it", "pt" };

        // English → Spanish | French | German | Italian | Portuguese.
        private static readonly (string En, string Words)[] Book =
        {
            ("hello", "hola|bonjour|hallo|ciao|olá"),
            ("hi", "hola|salut|hallo|ciao|oi"),
            ("goodbye", "adiós|au revoir|auf Wiedersehen|arrivederci|adeus"),
            ("bye", "adiós|salut|tschüss|ciao|tchau"),
            ("see you later", "hasta luego|à plus tard|bis später|a dopo|até logo"),
            ("see you tomorrow", "hasta mañana|à demain|bis morgen|a domani|até amanhã"),
            ("good morning", "buenos días|bonjour|guten Morgen|buongiorno|bom dia"),
            ("good afternoon", "buenas tardes|bon après-midi|guten Tag|buon pomeriggio|boa tarde"),
            ("good evening", "buenas noches|bonsoir|guten Abend|buonasera|boa noite"),
            ("good night", "buenas noches|bonne nuit|gute Nacht|buonanotte|boa noite"),
            ("please", "por favor|s'il vous plaît|bitte|per favore|por favor"),
            ("thank you", "gracias|merci|danke|grazie|obrigado (obrigada if you're a girl)"),
            ("thanks", "gracias|merci|danke|grazie|obrigado"),
            ("thank you very much", "muchas gracias|merci beaucoup|vielen Dank|grazie mille|muito obrigado"),
            ("you're welcome", "de nada|de rien|bitte schön|prego|de nada"),
            ("yes", "sí|oui|ja|sì|sim"),
            ("no", "no|non|nein|no|não"),
            ("maybe", "quizás|peut-être|vielleicht|forse|talvez"),
            ("sorry", "lo siento|désolé|Entschuldigung|mi dispiace|desculpe"),
            ("excuse me", "perdón|excusez-moi|Entschuldigung|mi scusi|com licença"),
            ("how are you", "¿cómo estás?|comment ça va ?|wie geht's?|come stai?|como você está?"),
            ("i'm fine", "estoy bien|je vais bien|mir geht's gut|sto bene|estou bem"),
            ("i'm good", "estoy bien|je vais bien|mir geht's gut|sto bene|estou bem"),
            ("what's your name", "¿cómo te llamas?|comment tu t'appelles ?|wie heißt du?|come ti chiami?|qual é o seu nome?"),
            ("my name is", "me llamo|je m'appelle|ich heiße|mi chiamo|meu nome é"),
            ("nice to meet you", "mucho gusto|enchanté|freut mich|piacere|prazer em conhecer você"),
            ("i love you", "te quiero (or te amo)|je t'aime|ich liebe dich|ti amo|eu te amo"),
            ("happy birthday", "feliz cumpleaños|joyeux anniversaire|alles Gute zum Geburtstag|buon compleanno|feliz aniversário"),
            ("merry christmas", "feliz Navidad|joyeux Noël|frohe Weihnachten|buon Natale|feliz Natal"),
            ("happy new year", "feliz año nuevo|bonne année|frohes neues Jahr|felice anno nuovo|feliz ano novo"),
            ("i don't understand", "no entiendo|je ne comprends pas|ich verstehe nicht|non capisco|não entendo"),
            ("do you speak english", "¿hablas inglés?|parlez-vous anglais ?|sprichst du Englisch?|parli inglese?|você fala inglês?"),
            ("where is the bathroom", "¿dónde está el baño?|où sont les toilettes ?|wo ist die Toilette?|dov'è il bagno?|onde fica o banheiro?"),
            ("how much does it cost", "¿cuánto cuesta?|combien ça coûte ?|wie viel kostet das?|quanto costa?|quanto custa?"),
            ("help", "ayuda|au secours|Hilfe|aiuto|socorro"),
            ("i'm hungry", "tengo hambre|j'ai faim|ich habe Hunger|ho fame|estou com fome"),
            ("i'm thirsty", "tengo sed|j'ai soif|ich habe Durst|ho sete|estou com sede"),
            ("i'm tired", "estoy cansado|je suis fatigué|ich bin müde|sono stanco|estou cansado"),
            ("let's go", "¡vamos!|allons-y !|los geht's!|andiamo!|vamos!"),
            ("good luck", "buena suerte|bonne chance|viel Glück|buona fortuna|boa sorte"),
            ("welcome", "bienvenido|bienvenue|willkommen|benvenuto|bem-vindo"),
            ("water", "agua|eau (l'eau)|Wasser|acqua|água"),
            ("food", "comida|nourriture|Essen|cibo|comida"),
            ("bread", "pan|pain|Brot|pane|pão"),
            ("milk", "leche|lait|Milch|latte|leite"),
            ("cheese", "queso|fromage|Käse|formaggio|queijo"),
            ("apple", "manzana|pomme|Apfel|mela|maçã"),
            ("banana", "plátano (or banana)|banane|Banane|banana|banana"),
            ("egg", "huevo|œuf|Ei|uovo|ovo"),
            ("chicken", "pollo|poulet|Hähnchen|pollo|frango"),
            ("chocolate", "chocolate|chocolat|Schokolade|cioccolato|chocolate"),
            ("ice cream", "helado|glace|Eis|gelato|sorvete"),
            ("cat", "gato|chat|Katze|gatto|gato"),
            ("dog", "perro|chien|Hund|cane|cachorro"),
            ("bird", "pájaro|oiseau|Vogel|uccello|pássaro"),
            ("horse", "caballo|cheval|Pferd|cavallo|cavalo"),
            ("fish", "pez (pescado when it's food)|poisson|Fisch|pesce|peixe"),
            ("friend", "amigo (amiga for a girl)|ami (amie for a girl)|Freund (Freundin for a girl)|amico (amica for a girl)|amigo (amiga for a girl)"),
            ("family", "familia|famille|Familie|famiglia|família"),
            ("mom", "mamá|maman|Mama|mamma|mãe"),
            ("dad", "papá|papa|Papa|papà|pai"),
            ("brother", "hermano|frère|Bruder|fratello|irmão"),
            ("sister", "hermana|sœur|Schwester|sorella|irmã"),
            ("school", "escuela|école|Schule|scuola|escola"),
            ("teacher", "maestro (maestra)|professeur|Lehrer (Lehrerin)|insegnante|professor (professora)"),
            ("book", "libro|livre|Buch|libro|livro"),
            ("house", "casa|maison|Haus|casa|casa"),
            ("car", "coche (carro in Latin America)|voiture|Auto|macchina|carro"),
            ("computer", "computadora (ordenador in Spain)|ordinateur|Computer|computer|computador"),
            ("phone", "teléfono|téléphone|Telefon|telefono|telefone"),
            ("game", "juego|jeu|Spiel|gioco|jogo"),
            ("music", "música|musique|Musik|musica|música"),
            ("red", "rojo|rouge|rot|rosso|vermelho"),
            ("blue", "azul|bleu|blau|blu|azul"),
            ("green", "verde|vert|grün|verde|verde"),
            ("yellow", "amarillo|jaune|gelb|giallo|amarelo"),
            ("black", "negro|noir|schwarz|nero|preto"),
            ("white", "blanco|blanc|weiß|bianco|branco"),
            ("one", "uno|un|eins|uno|um"),
            ("two", "dos|deux|zwei|due|dois"),
            ("three", "tres|trois|drei|tre|três"),
            ("four", "cuatro|quatre|vier|quattro|quatro"),
            ("five", "cinco|cinq|fünf|cinque|cinco"),
            ("six", "seis|six|sechs|sei|seis"),
            ("seven", "siete|sept|sieben|sette|sete"),
            ("eight", "ocho|huit|acht|otto|oito"),
            ("nine", "nueve|neuf|neun|nove|nove"),
            ("ten", "diez|dix|zehn|dieci|dez"),
            ("monday", "lunes|lundi|Montag|lunedì|segunda-feira"),
            ("tuesday", "martes|mardi|Dienstag|martedì|terça-feira"),
            ("wednesday", "miércoles|mercredi|Mittwoch|mercoledì|quarta-feira"),
            ("thursday", "jueves|jeudi|Donnerstag|giovedì|quinta-feira"),
            ("friday", "viernes|vendredi|Freitag|venerdì|sexta-feira"),
            ("saturday", "sábado|samedi|Samstag|sabato|sábado"),
            ("sunday", "domingo|dimanche|Sonntag|domenica|domingo"),
            ("today", "hoy|aujourd'hui|heute|oggi|hoje"),
            ("tomorrow", "mañana|demain|morgen|domani|amanhã"),
            ("day", "día|jour|Tag|giorno|dia"),
            ("night", "noche|nuit|Nacht|notte|noite"),
            ("sun", "sol|soleil|Sonne|sole|sol"),
            ("moon", "luna|lune|Mond|luna|lua"),
            ("happy", "feliz|heureux|glücklich|felice|feliz"),
            ("sad", "triste|triste|traurig|triste|triste"),
            ("big", "grande|grand|groß|grande|grande"),
            ("small", "pequeño|petit|klein|piccolo|pequeno"),
            ("good", "bueno|bon|gut|buono|bom"),
            ("bad", "malo|mauvais|schlecht|cattivo|mau"),
            ("beautiful", "hermoso|beau (belle)|schön|bello|bonito"),
        };

        private static readonly (string En, string Ja)[] Japanese =
        {
            ("hello", "こんにちは (konnichiwa)"), ("good morning", "おはよう (ohayou)"), ("good night", "おやすみ (oyasumi)"),
            ("goodbye", "さようなら (sayounara)"), ("thank you", "ありがとう (arigatou)"), ("please", "お願いします (onegaishimasu)"),
            ("yes", "はい (hai)"), ("no", "いいえ (iie)"), ("sorry", "ごめんなさい (gomen nasai)"), ("excuse me", "すみません (sumimasen)"),
            ("i love you", "愛してる (aishiteru)"), ("cat", "猫 (neko)"), ("dog", "犬 (inu)"), ("water", "水 (mizu)"),
            ("friend", "友達 (tomodachi)"), ("delicious", "おいしい (oishii)"), ("cute", "かわいい (kawaii)"),
        };

        private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["hey"] = "hi", ["good bye"] = "goodbye", ["bye bye"] = "bye", ["thankyou"] = "thank you", ["thanks a lot"] = "thank you very much",
            ["your welcome"] = "you're welcome", ["you are welcome"] = "you're welcome", ["how are you doing"] = "how are you", ["how's it going"] = "how are you",
            ["i am fine"] = "i'm fine", ["i am good"] = "i'm good", ["what is your name"] = "what's your name", ["where's the bathroom"] = "where is the bathroom",
            ["where is the toilet"] = "where is the bathroom", ["where is the restroom"] = "where is the bathroom", ["how much is it"] = "how much does it cost",
            ["how much is this"] = "how much does it cost", ["i do not understand"] = "i don't understand", ["i am hungry"] = "i'm hungry", ["i am thirsty"] = "i'm thirsty",
            ["i am tired"] = "i'm tired", ["mother"] = "mom", ["mum"] = "mom", ["father"] = "dad", ["kitty"] = "cat", ["puppy"] = "dog", ["home"] = "house",
            ["cellphone"] = "phone", ["cell phone"] = "phone", ["pc"] = "computer", ["happy bday"] = "happy birthday", ["sorry about that"] = "sorry",
            ["pretty"] = "beautiful", ["good day"] = "good afternoon", ["love you"] = "i love you",
        };

        /// <summary>What someone asked to translate, in which direction. Null if it isn't a translation request.</summary>
        public sealed record Ask(string Phrase, string LangWord, bool ToEnglish);

        private static readonly string LangNames = string.Join("|", Languages.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));

        public static Ask? Parse(string text)
        {
            string l = text.Trim().TrimEnd('?', '.', '!').Trim();
            string q(string s) => Regex.Replace(s.Trim(), @"^[""'“‘]+|[""'”’]+$", "").Trim();
            Match m;
            // "what does gracias mean" / "what does bonjour mean in english" / "what is gracias in english"
            m = Regex.Match(l, @"^what (?:does|do) ['""“]?(.+?)['""”]? mean(?: in english)?$", RegexOptions.IgnoreCase);
            if (m.Success) return new Ask(q(m.Groups[1].Value), "", true);
            m = Regex.Match(l, @"^(?:what(?:'s| is)|translate|how do you say) (.+?) (?:in|into|to) english$", RegexOptions.IgnoreCase);
            if (m.Success) return new Ask(q(m.Groups[1].Value), "", true);
            // "how do you say hello in spanish", "how to say", "how would i say"
            m = Regex.Match(l, @"^(?:how (?:do|would|can|should) (?:you|i|we) say|how to say|how is|how'?s) (.+) (?:in|en) ([a-z]+)$", RegexOptions.IgnoreCase);
            if (m.Success && !Regex.IsMatch(m.Groups[1].Value, @"^(it|that|the weather)$", RegexOptions.IgnoreCase)) return new Ask(q(m.Groups[1].Value), m.Groups[2].Value, false);
            // "translate thank you to french", "say i love you in portuguese"
            m = Regex.Match(l, @"^(?:translate|say|spell) (.+) (?:to|into|in) ([a-z]+)$", RegexOptions.IgnoreCase);
            if (m.Success && (Languages.ContainsKey(m.Groups[2].Value) || l.StartsWith("translate", StringComparison.OrdinalIgnoreCase))) return new Ask(q(m.Groups[1].Value), m.Groups[2].Value, false);
            // "what's the spanish word for dog"
            m = Regex.Match(l, @"^(?:what(?:'s| is) )?(?:the )?(" + LangNames + @") (?:word|phrase|way to say|for) (?:for )?(.+)$", RegexOptions.IgnoreCase);
            if (m.Success) return new Ask(q(m.Groups[2].Value), m.Groups[1].Value, false);
            // "what is cat in german", "hello in spanish"
            m = Regex.Match(l, @"^(?:what(?:'s| is) |whats )?(.+?) in (" + LangNames + @")$", RegexOptions.IgnoreCase);
            if (m.Success && m.Groups[1].Value.Split(' ').Length <= 6 && !Regex.IsMatch(m.Groups[1].Value, @"\d|\b(time|weather|capital|currency|population|language|money)\b", RegexOptions.IgnoreCase))
                return new Ask(q(m.Groups[1].Value), m.Groups[2].Value, false);
            // "translate hola" (no language): from another language into English.
            m = Regex.Match(l, @"^translate (.+)$", RegexOptions.IgnoreCase);
            if (m.Success) return new Ask(q(m.Groups[1].Value), "", true);
            return null;
        }

        private static string Key(string s)
        {
            s = Regex.Replace(s.ToLowerInvariant().Replace('’', '\''), @"[¿?¡!.,]", "").Trim();
            s = Regex.Replace(s, @"^(the word|the phrase|a|an|the)\s+", "");
            return Aliases.TryGetValue(s, out var a) ? a : s;
        }

        private static string Plain(string s)
        {
            var norm = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in norm) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return Regex.Replace(sb.ToString().ToLowerInvariant(), @"[¿?¡!.,]", "").Trim();
        }

        /// <summary>Every word in the phrase book (English and foreign), so the typo fixer leaves them alone.</summary>
        public static IEnumerable<string> Vocabulary =>
            Book.SelectMany(b => new[] { b.En }.Concat(b.Words.Split('|'))).Concat(Japanese.Select(j => j.En)).Concat(Languages.Keys);

        /// <summary>The offline translation of an English phrase, or null.</summary>
        public static string? Offline(string english, string code)
        {
            string k = Key(english);
            if (code == "ja") return Japanese.FirstOrDefault(j => j.En == k).Ja;
            int i = Array.IndexOf(Five, code);
            if (i < 0) return null;
            var hit = Book.FirstOrDefault(b => b.En == k);
            return hit.Words?.Split('|')[i];
        }

        /// <summary>A foreign word or phrase Ace knows, back to English: "gracias" → ("thank you", "Spanish").</summary>
        public static (string English, string Language)? Back(string foreign)
        {
            string p = Plain(foreign);
            if (p.Length == 0) return null;
            string[] names = { "Spanish", "French", "German", "Italian", "Portuguese" };
            foreach (var (en, words) in Book)
            {
                var list = words.Split('|');
                for (int i = 0; i < list.Length; i++)
                {
                    // "te quiero (or te amo)" → "te quiero", "te amo".
                    var options = Regex.Split(list[i], @"\s*\((?:or |and )?|\)").Select(Plain).Where(x => x.Length > 0 && !x.Contains(" for ") && !x.Contains(" in ") && !x.Contains(" if "));
                    if (options.Contains(p) && !string.Equals(p, en, StringComparison.OrdinalIgnoreCase)) return (en, names[i]);
                }
            }
            foreach (var (en, ja) in Japanese)
                if (Plain(ja).Contains(p) && p.Length >= 2) return (en, "Japanese");
            return null;
        }

        /// <summary>Asks the free MyMemory service. Null if offline or it failed.</summary>
        public static async Task<string?> WebAsync(string phrase, string from, string to, CancellationToken ct)
        {
            string url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(phrase)}&langpair={from}|{to}";
            var json = await Web.WebClient.FetchAsync(url, ct).ConfigureAwait(false);
            if (json == null) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("responseData", out var data)) return null;
                string? t = data.GetProperty("translatedText").GetString();
                if (string.IsNullOrWhiteSpace(t) || t.Contains("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase) || t.Contains("INVALID", StringComparison.Ordinal)) return null;
                return System.Net.WebUtility.HtmlDecode(t.Trim());
            }
            catch { return null; }
        }
    }
}
