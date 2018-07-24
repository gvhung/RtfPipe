using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using RtfPipe.Tokens;

namespace RtfPipe
{
  internal class HtmlWriter : IHtmlWriter
  {
    private readonly XmlWriter _writer;
    private readonly Stack<TagContext> _tags = new Stack<TagContext>();
    private readonly RtfHtmlSettings _settings;

    public Font DefaultFont { get; set; }
    public FontSize DefaultFontSize { get; } = new FontSize(UnitValue.FromHalfPoint(24));

    public HtmlWriter(XmlWriter xmlWriter, RtfHtmlSettings settings)
    {
      _writer = xmlWriter;
      _settings = settings ?? new RtfHtmlSettings();
    }

    public void AddText(FormatContext format, string text)
    {
      if (format.OfType<HiddenToken>().Any())
        return;
      EnsureSpans(format);
      _writer.WriteValue(text);
    }

    public void AddPicture(FormatContext format, Picture picture)
    {
      EnsureParagraph(format);
      var uri = _settings?.ImageUriGetter(picture);
      if (!string.IsNullOrEmpty(uri))
      {
        _writer.WriteStartElement("img");

        if (picture.WidthGoal.HasValue)
          _writer.WriteAttributeString("width", picture.WidthGoal.ToPx().ToString("0"));
        else if (picture.Width.HasValue)
          _writer.WriteAttributeString("width", picture.Width.ToPx().ToString("0"));

        if (picture.HeightGoal.HasValue)
          _writer.WriteAttributeString("height", picture.HeightGoal.ToPx().ToString("0"));
        else if (picture.Height.HasValue)
          _writer.WriteAttributeString("height", picture.Height.ToPx().ToString("0"));

        _writer.WriteAttributeString("src", uri);
        _writer.WriteEndElement();
      }
    }

    public void AddBreak(FormatContext format, IToken token)
    {
      if (token is ParagraphBreak)
      {
        EnsureParagraph(format);
        while (!IsParagraphTag(_tags.Peek().Name))
          EndTag();
        EndTag();
      }
      else if (token is SectionBreak)
      {
        EnsureSection(format);
        while (_tags.Peek().Name != "div")
          EndTag();
        EndTag();
      }
      else if (token is CellBreak)
      {
        EnsureParagraph(format);
        while (_tags.Peek().Name != "td")
          EndTag();
        EndTag();
      }
      else if (token is RowBreak)
      {
        while (_tags.Peek().Name != "tr")
          EndTag();
        EndTag();
      }
      else if (token is LineBreak)
      {
        _writer.WriteStartElement("br");
        _writer.WriteEndElement();
      }
    }

    public void Close()
    {
      while (_tags.Count > 0)
        EndTag();
    }

    private bool IsParagraphTag(string name)
    {
      return name == "p" || name == "li" || name == "td"
        || name == "h1" || name == "h2" || name == "h3"
        || name == "h4" || name == "h5" || name == "h6";
    }

    private void EnsureSection(FormatContext format)
    {
      if (_tags.Any(t => t.Name == "div"))
        return;

      var tag = new TagContext("div", _tags.SafePeek());
      tag.AddRange(format.Where(t => t.Type == TokenType.SectionFormat));
      tag.Add(DefaultFontSize);
      if (DefaultFont != null) tag.Add(DefaultFont);
      WriteTag(tag);
    }

    private void EnsureParagraph(FormatContext format)
    {
      if (IsParagraphTag(_tags.SafePeek()?.Name))
        return;

      EnsureSection(format);

      if (format.Any(t => t is ParagraphNumbering || t is ListLevelType))
      {
        while (!(_tags.Peek().Name == "div" || _tags.Peek().Name == "ul" || _tags.Peek().Name == "ol"))
          EndTag();

        if (!(_tags.Peek().Name == "ol" || _tags.Peek().Name == "ul"))
        {
          var numType = format.OfType<ListLevelType>().FirstOrDefault()?.Value
            ?? (format.OfType<NumberLevelBullet>().Any() ? (NumberingType?)NumberingType.Bullet : null)
            ?? format.OfType<NumberingTypeToken>().FirstOrDefault()?.Value
            ?? NumberingType.Bullet;

          var listTag = default(TagContext);
          if (numType == NumberingType.Bullet)
            listTag = new TagContext("ul", _tags.SafePeek());
          else
            listTag = new TagContext("ol", _tags.SafePeek());

          listTag.AddRange(format.Where(t => (t.Type == TokenType.ParagraphFormat || t.Type == TokenType.CharacterFormat)
            && !IsSpanElement(t)));
          WriteTag(listTag);

          switch (numType)
          {
            case NumberingType.LowerLetter:
              _writer.WriteAttributeString("type", "a");
              break;
            case NumberingType.LowerRoman:
              _writer.WriteAttributeString("type", "i");
              break;
            case NumberingType.UpperLetter:
              _writer.WriteAttributeString("type", "A");
              break;
            case NumberingType.UpperRoman:
              _writer.WriteAttributeString("type", "I");
              break;
          }
        }

        var tag = new TagContext("li", _tags.SafePeek());
        tag.AddRange(format.Where(t => (t.Type == TokenType.ParagraphFormat || t.Type == TokenType.CharacterFormat)
          && !IsSpanElement(t)));
        WriteTag(tag);
      }
      else if (format.InTable)
      {
        while (!(_tags.Peek().Name == "div" || _tags.Peek().Name == "table" || _tags.Peek().Name == "tr"))
          EndTag();

        var tag = default(TagContext);
        if (!(_tags.Peek().Name == "table" || _tags.Peek().Name == "tr"))
        {
          tag = new TagContext("table", _tags.SafePeek());
          WriteTag(tag);
        }

        if (_tags.Peek().Name == "table")
        {
          tag = new TagContext("tr", _tags.SafePeek());
          WriteTag(tag);
        }

        tag = new TagContext("td", _tags.SafePeek());
        tag.AddRange(format.Where(t => (t.Type == TokenType.ParagraphFormat || t.Type == TokenType.CharacterFormat)
          && !IsSpanElement(t)));
        WriteTag(tag);
      }
      else if (format.TryGetValue<OutlineLevel>(out var outline) && outline.Value >= 0 && outline.Value < 6)
      {
        var tagName = "h" + (outline.Value + 1);
        while (!(_tags.Peek().Name == "div" || _tags.Peek().Name == tagName))
          EndTag();

        var tag = new TagContext(tagName, _tags.SafePeek());
        tag.AddRange(format.Where(t => (t.Type == TokenType.ParagraphFormat || t.Type == TokenType.CharacterFormat)
          && !IsSpanElement(t)));
        WriteTag(tag);
      }
      else
      {
        while (_tags.Peek().Name != "div")
          EndTag();

        var tag = new TagContext("p", _tags.SafePeek());
        tag.AddRange(format.Where(t => (t.Type == TokenType.ParagraphFormat || t.Type == TokenType.CharacterFormat)
          && !IsSpanElement(t)));
        WriteTag(tag);
      }
    }

    private void EnsureSpans(FormatContext format)
    {
      EnsureParagraph(format);

      var existing = CharacterFormats(_tags.Peek());
      var requested = CharacterFormats(format);

      var intersection = existing.Intersect(requested).ToList();
      if (intersection.Count == existing.Count
        && intersection.Count == requested.Count)
      {
        return;
      }

      existing = existing.Where(t => !intersection.Contains(t) && IsSpanElement(t)).ToList();
      requested = requested.Where(t => !intersection.Contains(t)).ToList();

      if (existing.Count > 0 && _tags.Peek().Name != "p")
      {
        EndTag();
        EnsureSpans(format);
      }
      else if (TryGetValue<BoldToken, IToken>(requested, out var bold))
      {
        WriteSpanElement(format, "strong", bold, requested);
      }
      else if (TryGetValue<ItalicToken, IToken>(requested, out var italic))
      {
        WriteSpanElement(format, "em", italic, requested);
      }
      else if (TryGetValue<UnderlineToken, IToken>(requested, out var underline))
      {
        WriteSpanElement(format, "u", underline, requested);
      }
      else if (TryGetValue<StrikeToken, IToken>(requested, out var strike))
      {
        WriteSpanElement(format, "s", strike, requested);
      }
      else if (TryGetValue<SubStartToken, IToken>(requested, out var sub))
      {
        WriteSpanElement(format, "sub", sub, requested);
      }
      else if (TryGetValue<SuperStartToken, IToken>(requested, out var super))
      {
        WriteSpanElement(format, "super", super, requested);
      }
      else if (requested.Count > 0)
      {
        WriteSpan(format);
      }
    }

    private void WriteSpanElement(FormatContext format, string name, IToken spanToken, IEnumerable<IToken> requested)
    {
      var tag = new TagContext(name, _tags.Peek());
      if (requested.Any(t => IsSpanElement(t) && t != spanToken))
      {
        tag.Add(spanToken);
        WriteTag(tag);
        EnsureSpans(format);
      }
      else
      {
        tag.AddRange(requested);
        WriteTag(tag);
      }
    }

    private bool TryGetValue<T, TParent>(IEnumerable<TParent> list, out T value) where T : TParent
    {
      var found = list.OfType<T>().ToList();
      if (found.Count < 1)
      {
        value = default;
        return false;
      }
      else
      {
        value = found[0];
        return true;
      }
    }

    private void WriteSpan(FormatContext format)
    {
      var tag = new TagContext("span", _tags.SafePeek());
      tag.AddRange(CharacterFormats(format));
      WriteTag(tag);
    }

    private List<IToken> CharacterFormats(FormatContext context)
    {
      var tokens = (IEnumerable<IToken>)context;
      if (context is TagContext tag)
        tokens = tag.AllInherited();
      return tokens
        .Where(t => t.Type == TokenType.CharacterFormat)
        .ToList();
    }

    private bool IsSpanElement(IToken token)
    {
      return token is BoldToken
        || token is ItalicToken
        || token is UnderlineToken
        || token is StrikeToken
        || token is SubStartToken
        || token is SuperStartToken;
    }

    private void WriteTag(TagContext tag)
    {
      _writer.WriteStartElement(tag.Name);
      var style = tag.ToString();
      if (!string.IsNullOrEmpty(style))
        _writer.WriteAttributeString("style", style);
      _tags.Push(tag);
    }

    private void EndTag()
    {
      _tags.Pop();
      _writer.WriteEndElement();
    }

    private class TagContext : FormatContext
    {
      public string Name { get; }
      public TagContext Parent { get; }

      public TagContext(string name, TagContext parent)
      {
        Name = name;
        Parent = parent;
      }

      protected override void AddInternal(IToken token)
      {
        if (Parents().SelectMany(t => t).Contains(token))
          return;
        base.AddInternal(token);
      }

      public IEnumerable<TagContext> Parents()
      {
        var curr = this.Parent;
        while (curr != null)
        {
          yield return curr;
          curr = curr.Parent;
        }
      }

      public IEnumerable<TagContext> ParentsAndSelf()
      {
        var curr = this;
        while (curr != null)
        {
          yield return curr;
          curr = curr.Parent;
        }
      }

      public IEnumerable<IToken> AllInherited()
      {
        return ParentsAndSelf()
          .SelectMany(c => c);
      }

      public override string ToString()
      {
        var builder = new StringBuilder();
        foreach (var token in this)
        {
          if (token is Font font)
            WriteCss(builder, font);
          else if (token is FontSize fontSize)
            WriteCss(builder, "font-size", fontSize.Value.ToPt().ToString("0.#") + "pt");
          else if (token is BackgroundColor background)
            WriteCss(builder, "background", "#" + background.Value);
          else if (token is CapitalToken)
            WriteCss(builder, "text-transform", "uppercase");
          else if (token is ForegroundColor color)
            WriteCss(builder, "color", "#" + color.Value);
          else if (token is TextAlign align)
            WriteCss(builder, "text-align", align.Value.ToString().ToLowerInvariant());
        }
        return builder.ToString();
      }

      private void WriteCss(StringBuilder builder, Font font)
      {
        var name = font.Name.IndexOf(' ') > 0 ? "\"" + font.Name + "\"" : font.Name;
        switch (font.Category)
        {
          case FontFamilyCategory.Roman:
            name += ", serif";
            break;
          case FontFamilyCategory.Swiss:
            name += ", sans-serif";
            break;
          case FontFamilyCategory.Modern:
            name += ", monospace";
            break;
          case FontFamilyCategory.Script:
            name += ", cursive";
            break;
          case FontFamilyCategory.Decor:
            name += ", fantasy";
            break;
        }
        WriteCss(builder, "font-family", name);
      }

      private void WriteCss(StringBuilder builder, string property, string value)
      {
        builder.Append(property)
          .Append(":")
          .Append(value)
          .Append(";");
      }
    }
  }
}