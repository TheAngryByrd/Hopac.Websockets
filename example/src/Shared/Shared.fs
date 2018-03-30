namespace Shared

open System
type Counter = int

type Ticker = {
    Timestamp : DateTimeOffset
    Value     : int
    Name      : string
}
