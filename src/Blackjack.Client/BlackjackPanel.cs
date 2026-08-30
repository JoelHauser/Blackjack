using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Blackjack.Client
{
    /// <summary>
    /// The table.
    ///
    /// Built from Unity primitives rather than cloned from an EFT screen. Cloning was
    /// right for the menu button, where matching the game exactly is the point and the
    /// thing copied is one small widget; a whole screen drags in controllers that
    /// expect a session behind them. It does borrow the game's font, because a panel in
    /// Arial beside a menu in Bender looks broken.
    ///
    /// This side renders and asks. It never shuffles, never scores a hand and never
    /// decides an outcome -- every one of those lives on the server, and the dealer's
    /// hole card is not in the response until the hand is over, so it could not cheat
    /// even by accident.
    /// </summary>
    internal static class BlackjackPanel
    {
        private const string RootName = "BlackjackPanel";

        private static readonly Color Felt = new Color(0.055f, 0.29f, 0.16f, 1f);
        private static readonly Color FeltEdge = new Color(0.20f, 0.13f, 0.07f, 1f);
        private static readonly Color Ink = new Color(0.90f, 0.89f, 0.84f, 1f);
        private static readonly Color Faint = new Color(0.68f, 0.72f, 0.66f, 1f);
        private static readonly Color Good = new Color(0.55f, 0.80f, 0.45f, 1f);
        private static readonly Color Bad = new Color(0.85f, 0.35f, 0.32f, 1f);
        private static readonly Color ChipFace = new Color(0.16f, 0.17f, 0.18f, 1f);
        private static readonly Color ChipOn = new Color(0.62f, 0.50f, 0.14f, 1f);

        private static GameObject _root;
        private static TMP_FontAsset _font;

        private static TextMeshProUGUI _balance;
        private static TextMeshProUGUI _message;
        private static TextMeshProUGUI _dealerValue;
        private static TextMeshProUGUI _wagerLabel;
        private static RectTransform _dealerCards;
        private static RectTransform _handsRow;
        private static RectTransform _walletRow;
        private static RectTransform _actionRow;
        private static GameObject _betControls;

        private static readonly List<(string Wallet, GameObject Chip)> _wallets = new List<(string, GameObject)>();

        private static TMP_InputField _wagerInput;

        /// <summary>
        /// What the player holds, as of the last time the server said. Used to warn
        /// before a bet is sent and to size ALL IN. The server checks again regardless;
        /// this only saves a pointless round trip and a confusing refusal.
        /// </summary>
        private static readonly Dictionary<string, long> _balances = new Dictionary<string, long>();

        private static string _wallet = "Roubles";
        private static int _wager = 10_000;

        internal static bool IsOpen => _root != null && _root.activeSelf;

        internal static void Toggle()
        {
            if (IsOpen)
            {
                Close();
                return;
            }

            Open();
        }

        internal static void Open()
        {
            try
            {
                if (_root == null)
                {
                    Build();
                }

                if (_root == null)
                {
                    return;
                }

                _root.SetActive(true);

                RefreshBalances();

                // Resume rather than assume: a hand can still be live from a previous
                // visit, and /blackjack/state is what says so.
                Render(BlackjackApi.State(), "");
            }
            catch (Exception ex)
            {
                BlackjackClientPlugin.Log.LogError("[Blackjack] could not open the table: " + ex);
            }
        }

        internal static void Close()
        {
            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        // ------------------------------------------------------------------ actions

        private static void Deal()
        {
            // Caught here only to save a round trip and a refusal that reads worse than
            // this does. The server checks the balance itself and is the authority.
            if (_balances.TryGetValue(_wallet, out var held) && _wager > held)
            {
                _message.text = $"You have {held:N0} {Short(_wallet)}. That bet is more than you are carrying.";
                _message.color = Bad;
                return;
            }

            Render(BlackjackApi.Deal(_wallet, _wager), $"Dealing {_wager:N0} {_wallet}...");
        }

        private static void Act(string action)
        {
            Render(BlackjackApi.Act(action), action + "...");
        }

        private static void ChooseWallet(string wallet)
        {
            _wallet = wallet;

            // Valuables are staked by the piece and currency in thousands, so a wager
            // carried over from roubles is nonsense in bitcoin. This is a convenience,
            // not a rule -- the server owns the limits and will say so if this is out
            // of range.
            _wager = IsValuable(wallet) ? 1 : 10_000;

            if (_wagerInput != null)
            {
                _wagerInput.SetTextWithoutNotify(_wager.ToString());
            }

            UpdateWagerLabel();
            HighlightWallet();
        }

        private static bool IsValuable(string wallet) =>
            wallet == "Bitcoin" || wallet == "LegaMedals" || wallet == "GpCoins";

        // ------------------------------------------------------------------ rendering

        /// <summary>
        /// Draws whatever the server just said. Everything visible here comes out of
        /// that response; nothing is inferred locally.
        /// </summary>
        private static void Render(JObject response, string pending)
        {
            if (response == null)
            {
                _message.text = "<color=#d9534f>No answer from the server.</color>  Is it running?";
                _message.color = Bad;
                return;
            }

            var ok = response["Ok"]?.ToObject<bool>() ?? false;
            var error = response["Error"]?.ToString();
            var note = response["Note"]?.ToString();

            if (!ok && !string.IsNullOrEmpty(error))
            {
                _message.text = error;
                _message.color = Bad;
            }
            else if (!string.IsNullOrEmpty(note))
            {
                _message.text = note;
                _message.color = Faint;
            }
            else
            {
                _message.text = string.IsNullOrEmpty(pending) ? "" : "";
                _message.color = Faint;
            }

            var balance = response["Balance"]?.ToObject<long>();
            var wallet = response["Wallet"]?.ToString();
            if (balance.HasValue)
            {
                _balance.text = $"{balance.Value:N0}  {wallet}";
            }

            // A settled hand has moved money, so what the player holds has changed.
            var round = response["Round"] as JObject;
            if ((round?["Phase"]?.ToString() ?? "") == "Settled")
            {
                RefreshBalances();
            }

            RenderRound(round);
        }

        private static void RenderRound(JObject round)
        {
            Clear(_dealerCards);
            Clear(_handsRow);

            var phase = round?["Phase"]?.ToString() ?? "AwaitingBet";
            var betting = phase == "AwaitingBet" || phase == "Settled";

            _betControls.SetActive(betting);

            // Dealer
            var dealer = round?["Dealer"] as JObject;
            var dealerCards = dealer?["Cards"]?.ToObject<List<string>>() ?? new List<string>();
            foreach (var card in dealerCards)
            {
                CardView.Build(_dealerCards, card, _font);
            }

            if (phase == "PlayerTurn" && dealerCards.Count > 0)
            {
                // The hole card is not in the response during play. Drawing a back in
                // its place is honest: the client does not have it to show.
                CardView.Build(_dealerCards, null, _font);
            }

            var dealerValue = dealer?["Value"]?.ToObject<int>() ?? 0;
            _dealerValue.text = dealerCards.Count == 0
                ? ""
                : (phase == "PlayerTurn" ? $"{dealerValue} + ?" : dealerValue.ToString());

            // Player hands
            var hands = round?["PlayerHands"] as JArray;
            if (hands != null)
            {
                var active = round["ActiveHandIndex"]?.ToObject<int>() ?? -1;
                for (var i = 0; i < hands.Count; i++)
                {
                    BuildHand(_handsRow, (JObject)hands[i], i == active && phase == "PlayerTurn");
                }
            }

            RenderActions(round, phase);
        }

        private static void BuildHand(RectTransform parent, JObject hand, bool isActive)
        {
            var column = NewPanel("Hand", parent, new Color(0f, 0f, 0f, isActive ? 0.22f : 0f));
            var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = 8f;
            layout.padding = new RectOffset(14, 14, 10, 10);
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;

            var fitter = column.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var cardsRow = NewRow("Cards", column, 12f);
            foreach (var card in hand["Cards"]?.ToObject<List<string>>() ?? new List<string>())
            {
                CardView.Build(cardsRow, card, _font);
            }

            var value = hand["Value"]?.ToObject<int>() ?? 0;
            var soft = hand["IsSoft"]?.ToObject<bool>() ?? false;
            var outcome = hand["Outcome"]?.ToString();
            var wager = hand["Wager"]?.ToObject<long>() ?? 0;

            var line = Label(column, $"{value}{(soft ? " soft" : "")}", 26f, Ink, TextAlignmentOptions.Center);
            line.rectTransform.sizeDelta = new Vector2(0f, 32f);

            var staked = Label(column, $"staked {wager:N0}", 18f, Faint, TextAlignmentOptions.Center);
            staked.rectTransform.sizeDelta = new Vector2(0f, 24f);

            if (!string.IsNullOrEmpty(outcome) && outcome != "Pending")
            {
                var won = outcome == "Win" || outcome == "Blackjack";
                var pushed = outcome == "Push";
                var result = Label(
                    column,
                    outcome.ToUpperInvariant(),
                    24f,
                    pushed ? Faint : (won ? Good : Bad),
                    TextAlignmentOptions.Center);
                result.rectTransform.sizeDelta = new Vector2(0f, 30f);
            }
        }

        private static void RenderActions(JObject round, string phase)
        {
            Clear(_actionRow);

            if (phase == "PlayerTurn")
            {
                var actions = round?["AvailableActions"]?.ToObject<List<string>>() ?? new List<string>();

                // A fixed order, not the server's. Hit and Stand are muscle memory and
                // should not move about because the legal set changed.
                foreach (var action in new[] { "Hit", "Stand", "Double", "Split" })
                {
                    if (!actions.Contains(action))
                    {
                        continue;
                    }

                    var captured = action;
                    Chip(_actionRow, action.ToUpperInvariant(), 150f, () => Act(captured));
                }

                return;
            }

            Chip(_actionRow, "DEAL", 190f, Deal);
        }

        private static void RefreshBalances()
        {
            var ping = BlackjackApi.Ping();
            var balances = ping?["Balances"];
            if (balances == null)
            {
                return;
            }

            _balances.Clear();
            foreach (var entry in balances.Children<JProperty>())
            {
                _balances[entry.Name] = entry.Value.ToObject<long>();
            }

            UpdateWagerLabel();
        }

        private static void SetPreferredHeight(RectTransform rect, float height)
        {
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
        }

        private static void SetPreferredSize(RectTransform rect, float width, float height)
        {
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.minWidth = width;
            element.preferredHeight = height;
            element.minHeight = height;
        }

        // ------------------------------------------------------------------ building

        private static void Build()
        {
            _font = BorrowFont();

            var canvasObject = new GameObject(RootName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // Match height, not a blend of both. Blending makes the table grow with
            // screen width, so an ultrawide gets a table stretched across it while a
            // 16:9 screen gets a smaller one -- the same layout at two different sizes.
            // Tying it to height keeps the table one size and lets the extra width on a
            // wide monitor stay as empty space around it, which is what a table sitting
            // on a floor actually looks like.
            scaler.matchWidthOrHeight = 1f;

            _root = canvasObject;

            // Dimmed backdrop, which also swallows clicks so the menu behind cannot be
            // operated while the table is open.
            var backdrop = NewPanel("Backdrop", canvasObject.transform, new Color(0f, 0f, 0f, 0.82f));
            Stretch(backdrop);

            // The table: a dark wooden rim with felt inside it.
            var rim = NewPanel("Rim", canvasObject.transform, FeltEdge);
            rim.anchorMin = rim.anchorMax = new Vector2(0.5f, 0.5f);
            rim.pivot = new Vector2(0.5f, 0.5f);
            rim.sizeDelta = new Vector2(1500f, 900f);

            var felt = NewPanel("Felt", rim, Felt);
            felt.anchorMin = Vector2.zero;
            felt.anchorMax = Vector2.one;
            felt.offsetMin = new Vector2(14f, 14f);
            felt.offsetMax = new Vector2(-14f, -14f);

            BuildHeader(felt);
            BuildDealer(felt);
            BuildRules(felt);
            BuildPlayerArea(felt);
            BuildBetting(felt);
            BuildFooter(felt);

            BlackjackClientPlugin.Log.LogInfo("[Blackjack] table built");
        }

        private static void BuildHeader(RectTransform felt)
        {
            var title = Label(felt, "BLACKJACK", 30f, Ink, TextAlignmentOptions.Left);
            Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(30f, -54f), new Vector2(0f, 44f));

            _balance = Label(felt, "", 24f, Ink, TextAlignmentOptions.Right);
            Anchor(_balance.rectTransform, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(-30f, -54f), new Vector2(0f, 44f));
        }

        private static void BuildDealer(RectTransform felt)
        {
            var label = Label(felt, "DEALER", 20f, Faint, TextAlignmentOptions.Center);
            Anchor(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -104f), new Vector2(400f, 26f));

            _dealerCards = NewRow("DealerCards", felt, 12f);
            Anchor(_dealerCards, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(900f, CardView.Height));

            _dealerValue = Label(felt, "", 26f, Ink, TextAlignmentOptions.Center);
            Anchor(_dealerValue.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -292f), new Vector2(300f, 32f));
        }

        private static void BuildRules(RectTransform felt)
        {
            var line = Label(felt, "BLACKJACK PAYS 3 TO 2      DEALER MUST STAND ON 17", 19f, new Color(0.75f, 0.70f, 0.45f, 0.85f), TextAlignmentOptions.Center);
            Anchor(line.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(1000f, 28f));
        }

        private static void BuildPlayerArea(RectTransform felt)
        {
            _handsRow = NewRow("Hands", felt, 40f);
            Anchor(_handsRow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(1300f, 260f));
        }

        /// <summary>
        /// Wallet chips on one row, the wager on the next.
        ///
        /// Both rows are laid out by Unity rather than by arithmetic. The first attempt
        /// positioned every control by hand inside one bar, mixing anchors as it went,
        /// and they landed on top of each other. A layout group cannot make that
        /// mistake.
        /// </summary>
        private static void BuildBetting(RectTransform felt)
        {
            var holder = NewPanel("Betting", felt, new Color(0f, 0f, 0f, 0.18f));
            Anchor(holder, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 208f), new Vector2(1320f, 130f));
            _betControls = holder.gameObject;

            var column = holder.gameObject.AddComponent<VerticalLayoutGroup>();
            column.childAlignment = TextAnchor.MiddleCenter;
            column.spacing = 10f;
            column.padding = new RectOffset(16, 16, 12, 12);
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;
            column.childControlWidth = true;
            column.childControlHeight = true;

            _walletRow = NewRow("Wallets", holder, 8f);
            SetPreferredHeight(_walletRow, 46f);

            foreach (var wallet in new[] { "Roubles", "Dollars", "Euros", "GpCoins", "Bitcoin", "LegaMedals" })
            {
                var captured = wallet;
                var chip = Chip(_walletRow, Short(wallet), 132f, () => ChooseWallet(captured));
                _wallets.Add((wallet, chip));
            }

            var betRow = NewRow("Bet", holder, 12f);
            SetPreferredHeight(betRow, 48f);

            var caption = Label(betRow, "BET", 20f, Faint, TextAlignmentOptions.Right);
            SetPreferredSize(caption.rectTransform, 60f, 34f);

            BuildWagerInput(betRow);

            Chip(betRow, "ALL IN", 120f, BetEverything);

            _wagerLabel = Label(betRow, "", 20f, Faint, TextAlignmentOptions.Left);
            SetPreferredSize(_wagerLabel.rectTransform, 420f, 34f);

            HighlightWallet();
            UpdateWagerLabel();
        }

        /// <summary>
        /// A field the player types into, rather than a stepper they wear out. Getting
        /// from a thousand to five hundred thousand was ninety clicks.
        /// </summary>
        private static void BuildWagerInput(Transform parent)
        {
            var frame = NewPanel("WagerInput", parent, new Color(0.10f, 0.11f, 0.11f, 1f));
            SetPreferredSize(frame, 260f, 44f);

            var viewport = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(frame, false);
            var viewportRect = (RectTransform)viewport.transform;
            Stretch(viewportRect);
            viewportRect.offsetMin = new Vector2(12f, 4f);
            viewportRect.offsetMax = new Vector2(-12f, -4f);

            var text = Label(viewportRect, string.Empty, 24f, Ink, TextAlignmentOptions.Left);
            Stretch(text.rectTransform);
            text.raycastTarget = true;

            var input = frame.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = viewportRect;
            input.textComponent = text;
            input.fontAsset = _font;
            input.pointSize = 24f;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.characterLimit = 12;
            input.restoreOriginalTextOnEscape = true;
            input.text = _wager.ToString();
            input.onValueChanged.AddListener(OnWagerTyped);

            _wagerInput = input;
        }

        private static void OnWagerTyped(string typed)
        {
            // An empty or half-typed box is not an error worth shouting about; it is
            // simply not a bet yet.
            if (int.TryParse(typed, out var value) && value > 0)
            {
                _wager = value;
            }

            UpdateWagerLabel();
        }

        /// <summary>
        /// Everything in the chosen wallet. Whether that is a legal bet is still the
        /// server's call -- most wallets cap well below a full stash.
        /// </summary>
        private static void BetEverything()
        {
            if (!_balances.TryGetValue(_wallet, out var held) || held <= 0)
            {
                return;
            }

            _wager = (int)Math.Min(held, int.MaxValue);

            if (_wagerInput != null)
            {
                _wagerInput.SetTextWithoutNotify(_wager.ToString());
            }

            UpdateWagerLabel();
        }

        private static void BuildFooter(RectTransform felt)
        {
            _actionRow = NewRow("Actions", felt, 14f);
            Anchor(_actionRow, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 92f), new Vector2(1200f, 54f));

            _message = Label(felt, "", 20f, Faint, TextAlignmentOptions.Center);
            Anchor(_message.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 52f), new Vector2(1300f, 26f));

            Chip(felt, "LEAVE TABLE", 200f, Close, null, new Vector2(0.5f, 0f), new Vector2(0f, 18f));
        }

        // ------------------------------------------------------------------ widgets

        /// <summary>
        /// Says what is held and whether the bet fits inside it. Advisory: the server
        /// decides, and it also enforces per-wallet minimums and maximums this does not
        /// know about.
        /// </summary>
        private static void UpdateWagerLabel()
        {
            if (_wagerLabel == null)
            {
                return;
            }

            if (!_balances.TryGetValue(_wallet, out var held))
            {
                _wagerLabel.text = "";
                return;
            }

            if (_wager > held)
            {
                _wagerLabel.text = $"you have {held:N0} -- not enough";
                _wagerLabel.color = Bad;
                return;
            }

            _wagerLabel.text = $"you have {held:N0}";
            _wagerLabel.color = Faint;
        }

        private static void HighlightWallet()
        {
            foreach (var (wallet, chip) in _wallets)
            {
                var image = chip?.GetComponent<Image>();
                if (image != null)
                {
                    image.color = wallet == _wallet ? ChipOn : ChipFace;
                }
            }
        }

        private static string Short(string wallet) => wallet switch
        {
            "Roubles" => "RUB",
            "Dollars" => "USD",
            "Euros" => "EUR",
            "GpCoins" => "GP",
            "Bitcoin" => "BTC",
            "LegaMedals" => "LEGA",
            _ => wallet.ToUpperInvariant(),
        };

        private static GameObject Chip(
            Transform parent,
            string text,
            float width,
            Action onClick,
            Vector2? anchoredPosition = null,
            Vector2? anchor = null,
            Vector2? offset = null)
        {
            var rect = NewPanel("Chip_" + text, parent, ChipFace);
            rect.sizeDelta = new Vector2(width, 46f);

            if (anchor.HasValue)
            {
                rect.anchorMin = rect.anchorMax = anchor.Value;
                rect.pivot = anchor.Value;
                rect.anchoredPosition = offset ?? Vector2.zero;
                rect.sizeDelta = new Vector2(width, 46f);
            }
            else if (anchoredPosition.HasValue)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = anchoredPosition.Value;
            }

            var label = Label(rect, text, 21f, Ink, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            button.onClick.AddListener(() => onClick());

            return rect.gameObject;
        }

        private static TextMeshProUGUI Label(Transform parent, string text, float size, Color colour, TextAlignmentOptions align)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var label = go.AddComponent<TextMeshProUGUI>();
            if (_font != null)
            {
                label.font = _font;
            }

            label.text = text;
            label.fontSize = size;
            label.color = colour;
            label.alignment = align;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            return label;
        }

        private static RectTransform NewPanel(string name, Transform parent, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = colour;
            return (RectTransform)go.transform;
        }

        private static RectTransform NewRow(string name, Transform parent, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            return (RectTransform)go.transform;
        }

        private static void Anchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Clear(RectTransform parent)
        {
            if (parent == null)
            {
                return;
            }

            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
            }
        }

        /// <summary>
        /// EFT's own UI font, taken off a label already on screen. Falling back to TMP's
        /// default keeps the table readable rather than blank if none can be found.
        /// </summary>
        private static TMP_FontAsset BorrowFont()
        {
            try
            {
                var label = UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>()
                    .FirstOrDefault(t => t != null && t.font != null);

                if (label != null)
                {
                    return label.font;
                }
            }
            catch (Exception ex)
            {
                BlackjackClientPlugin.Log.LogWarning("[Blackjack] could not borrow a font: " + ex.Message);
            }

            return TMP_Settings.defaultFontAsset;
        }
    }
}
