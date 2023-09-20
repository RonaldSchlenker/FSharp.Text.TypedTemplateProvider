
open System

type Parser<'value> = Parser of (ParserInput -> ParseResult<'value>)
and [<Struct>] PVal<'value> =
    {
        index: int
        value: 'value
    }
and ParserInput = PVal<string>
and ParseResult<'out> = 
    | POk of PVal<'out>
    | PError of ParseError
and [<Struct>] ParseError =
    { 
        index: int
        message: string 
    }
and [<Struct>] DocPos =
    {
        index: int
        line: int
        column: int 
    }

module String =
    let equalsAt (index: int) (compareWith: string) (value: string) =
        index + compareWith.Length <= value.Length
        && value.Substring(index, compareWith.Length) = compareWith

module DocPos =
    
    // TODO: Perf: The parser combinators could track that, instead of computing it from scratch.
    let create (index: int) (input: string) =
        if index < 0 || index > input.Length then
            failwithf "Index %d is out of range of input string of length %d." index input.Length
        let lineStart,columnStart = 1,1
        let rec findLineAndColumn (currIdx: int) (line: int) (column: int) =
            if currIdx = index then
                { index = index; line = line; column = column }
            else
                let line,column =
                    if input |> String.equalsAt currIdx "\n"
                    then line + 1, columnStart
                    else line, column + 1
                findLineAndColumn (currIdx + 1) line column
        findLineAndColumn 0 lineStart columnStart

    let ofInput (pi: ParserInput) = create pi.index pi.value

let inline mkParser parser = Parser parser
let inline getParser (Parser parser) = parser

let inline bind
    ([<InlineIfLambda>] f: 'a -> Parser<_>)
    (parser: Parser<_>) 
    =
    mkParser <| fun inp ->
        match (getParser parser) inp with
        | POk pRes ->
            let fParser = getParser (f pRes.value)
            let fResult = fParser { inp with index = pRes.index }
            fResult
        | PError errors -> PError errors

let return' value = 
    mkParser <| fun inp ->
        POk { index = inp.index; value = value }

let zero = return' ()

let combine (p1: Parser<_>) (p2: Parser<_>) =
    mkParser <| fun inp ->
        let p1Res = (getParser p1) inp
        match p1Res with
        | POk p1Res -> (getParser p2) { inp with index = p1Res.index }
        | _ -> p1Res

type [<Struct>] WhileState<'a> = 
    { 
        index: int
        results: 'a list
        error: ParseError option
    }

// General problem: Should it be disallowed having a value, but not consuming?

let while' mkRes guard body =
    mkParser <| fun inp ->
        let rec iter currResults currIdx =
            let result = guard { index = currIdx; results = currResults; error = None }
            match result with
            | None ->
                let pBody = getParser (body ())
                match pBody { inp with index = currIdx } with
                | PError error ->
                    mkRes { index = currIdx; results = currResults; error = Some error }
                | POk res ->
                    if res.index > currIdx then
                        mkRes { index = currIdx; results = currResults; error = None }
                    else
                        iter (res.value :: currResults) res.index
            | Some result -> result
        iter [] inp.index

let whileCond (guard: unit -> bool) body =
    mkParser <| fun inp ->
        let rec iter currResults currIdx =
            match guard () with
            | true ->
                let pBody = getParser (body ())
                match pBody { inp with index = currIdx } with
                | PError errors -> PError errors
                | POk res ->
                    let hasConsumed = res.index > currIdx
                    if hasConsumed then
                        POk { index = currIdx; value = currResults }
                    else
                        iter (res.value :: currResults) res.index
            | false -> POk { index = currIdx; value = currResults }
        iter [] inp.index

type CanParse = CanParse

let whileCanParse (_: unit -> CanParse) body =
    mkParser <| fun inp ->
        let rec iter currResults currIdx =
            let pBody = getParser (body ())
            match pBody { inp with index = currIdx } with
            | PError _ ->
                POk { index = currIdx; value = currResults }
            | POk res ->
                let hasConsumed = res.index > currIdx
                if not hasConsumed then
                    POk { index = currIdx; value = currResults }
                else
                    iter (res.value :: currResults) res.index
        iter [] inp.index

let inline run (value: string) (parser: Parser<_>) =
    let inp = { index = 0; value = value }
    (getParser parser) inp

let pmaybe defaultValue (p: Parser<_>) =
    mkParser <| fun inp ->
        match (getParser p) inp with
        | POk pRes -> POk pRes
        | PError _ -> POk { index = inp.index; value = defaultValue }

let pstr (s: string) =
    mkParser <| fun inp ->
        let res =
            inp.index + s.Length <= inp.value.Length 
            && inp.value.Substring(inp.index, s.Length) = s 
        match res with
        | true -> POk { index = inp.index + s.Length; value = s }
        | false -> PError { index = inp.index; message = $"Expected: '{s}'" }

let ( ~+ ) value = pstr value
let ( ~- ) value = pmaybe (pstr value)

type ParserBuilderBase() =
    member inline _.Bind(p, [<InlineIfLambda>] f) = bind f p
    member _.Return(value) = return' value
    member _.ReturnFrom(value) = value
    member _.Yield(value) = return' value
    member _.YieldFrom(value) = value
    member _.Zero() = zero
    member _.Combine(p1, fp2) = combine p1 (fp2 ())
    member _.Delay(f) = f
    member _.While(guard: unit -> bool, body) = whileCond guard body
    member _.While(guard: unit -> CanParse, body) = whileCanParse guard body
    member _.For(sequence: _ seq, body) =
        let enum = sequence.GetEnumerator()
        whileCond (fun _ -> enum.MoveNext()) (fun () -> body enum.Current)

type ParserBuilder() =
    inherit ParserBuilderBase()
    member _.Run(f) = f ()

let parse = ParserBuilder()

type ParserConcatBuilder() =
    inherit ParserBuilderBase()
    member _.Run(f) =
        parse {
            let! res = f ()
            return res |> String.concat ""
        }

let concat = ParserConcatBuilder()

let map f (p: Parser<_>) =
    parse {
        let! x = p
        return f x
    }

let pignore (p: Parser<_>) =
    parse {
        let! _ = p
        return ()
    }

let pblank = pstr " "

//let puntil (untilParser: Parser<_>) =
//    parse {
//        let x = parse {
//        }
//    }

let pblanks =
    mkParser <| fun inp ->
        let mutable currIdx = inp.index
        while currIdx < inp.value.Length && inp.value.[currIdx] = ' ' do
            do currIdx <- currIdx + 1
        POk { index = currIdx; value = () }

parse {
    let! start = +"["
    let! a = parse {
        for x in 1..10 do
            let! x = +"x"
            yield x
        //while true do
        //    let! x = % "x"
        //    yield x
    }

    return "YES: start"
}
|> run " {{xxx"

// TODO: SepBy



// --------------------------------------------------------------------
// EXAMPLES
// --------------------------------------------------------------------

let shouldParse (input: string) expected (parser: Parser<_>) =
    match run input parser with
    | POk res ->
        if res.value <> expected then
            failwithf "Input was parsed, but result value was not as expected: '%A' but got '%A'." 
                expected
                res.value
        res.value
    | PError errors -> failwithf "ERROR: %A" errors

parse { while CanParse do pstr " " }
|> run "         XX"

//|> shouldParse "      XX" "    "

//parse {
//    while CanParse do
//        let x = pstr ""
//        yield x
//}
    