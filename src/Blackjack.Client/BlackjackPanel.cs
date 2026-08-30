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

            UpdateWagerLabel();
            HighlightWallet();
        }

        private static void StepWager(int direction)
        {
            var step = IsValuable(_wallet) ? 1 : 1_000;
            _wager = Mathf.Max(step, _wager + (direction * step));
            UpdateWagerLabel();
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

            var round = response["Round"] as JObject;
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
            scaler.matchWidthOrHeight = 0.5f;

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

        private static void BuildBetting(RectTransform felt)
        {
            var holder = NewPanel("Betting", felt, new Color(0f, 0f, 0f, 0.18f));
            Anchor(holder, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 176f), new Vector2(1300f, 96f));
            _betControls = holder.gameObject;

            _walletRow = NewRow("Wallets", holder, 8f);
            Anchor(_walletRow, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(430f, 0f), new Vector2(840f, 46f));
            _walletRow.pivot = new Vector2(0.5f, 0.5f);

            foreach (var wallet in new[] { "Roubles", "Dollars", "Euros", "GpCoins", "Bitcoin", "LegaMedals" })
            {
                var captured = wallet;
                var chip = Chip(_walletRow, Short(wallet), 128f, () => ChooseWallet(captured));
                _wallets.Add((wallet, chip));
            }

            // Wager stepper. The step is a convenience for the mouse, not a rule: the
            // server owns the limits and refuses anything outside them by name.
            Chip(holder, "-", 54f, () => StepWager(-1), new Vector2(-560f, -26f));
            _wagerLabel = Label(holder, "", 26f, Ink, TextAlignmentOptions.Center);
            Anchor(_wagerLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-400f, -26f), new Vector2(260f, 34f));
            Chip(holder, "+", 54f, () => StepWager(1), new Vector2(-240f, -26f));

            HighlightWallet();
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

        private static void UpdateWagerLabel()
        {
            if (_wagerLabel != null)
            {
                _wagerLabel.text = _wager.ToString("N0");
            }
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
